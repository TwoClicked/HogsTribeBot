using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using TribeBot.Bot.UI; // <-- EmbedHelper
using TribeBot.Core.Entities;
using TribeBot.Core.Interfaces;
using TribeBot.Data.Interfaces;
using TribeBot.Services.Services;

namespace TribeBot.Bot.Handlers
{
    public class BankHandler
    {
        private readonly DiscordSocketClient _client;
        private readonly IMemberService _memberService;
        private readonly IDonationService _donationService;
        private readonly IFineService _fineService;
        private readonly PaddleOcrServerService _ocrService;
        private readonly IGoogleSheetsDataStore _dataStore;

        private readonly Dictionary<ulong, string> _payForOverride = new();

        private const ulong DonationChannelId = 1440050111353721053;
        private const ulong FinePaymentChannelId = 1440431172160061450;
        private const ulong OfficerRoleId = 1222665812775534592;
        private const ulong GuildId = 1109193500664287336;
        private const ulong OfficerLogChannelId = 1440211043820507217;

        public BankHandler(
            DiscordSocketClient client,
            IMemberService memberService,
            IDonationService donationService,
            IFineService fineService,
            PaddleOcrServerService ocrService,
            IGoogleSheetsDataStore dataStore)
        {
            _client = client;
            _memberService = memberService;
            _donationService = donationService;
            _fineService = fineService;
            _ocrService = ocrService;
            _dataStore = dataStore;
        }

        private IMessageChannel OfficerLog =>
            _client.GetChannel(OfficerLogChannelId) as IMessageChannel;

        private Task LogOfficer(string title, Dictionary<string, string> fields)
            => OfficerLog?.SendMessageAsync(embed: EmbedHelper.Log(title, fields))
               ?? Task.CompletedTask;

        private const int BankFineAmount = 75_000_000;

        // ===================================================================
        // ROOT ENTRY
        // ===================================================================
        public async Task<bool> TryHandleAsync(SocketMessage message)
        {
            if (message.Author.IsBot)
                return false;

            string content = message.Content.Trim().ToLower();

            if (content == "!bankunpaid")
            {
                await ShowUnpaid(message);
                return true;
            }

            if (content == "!checkbank")
            {
                await CheckUserDonation(message);
                return true;
            }

            if (content == "!bankreminder")
            {
                await SendBankReminder(message);
                return true;
            }

            if (content.StartsWith("!payfor"))
            {
                await HandlePayFor(message);
                return true;
            }

            if (message.Channel.Id == DonationChannelId && message.Attachments.Any())
            {
                await HandleDonationUpload(message);
                return true;
            }
            if (content.StartsWith("!bankfine"))
            {
                await HandleBankFineCommand(message);
                return true;
            }


            return false;
        }



        // ===================================================================
        // !BANKUNPAID
        // ===================================================================
        private async Task ShowUnpaid(SocketMessage message)
        {
            var members = await _memberService.GetAllMembersAsync();
            var totals = await _donationService.GetTotalsForAllUsersThisWeekAsync();

            var unpaid = members
                .Where(m => !m.IsExempt &&
                            (!totals.ContainsKey(m.DiscordUserId) ||
                             totals[m.DiscordUserId] <= 0))
                .ToList();

            if (!unpaid.Any())
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Success("🎉 Everyone has paid or is exempt!"));
                return;
            }

            string list = string.Join("\n", unpaid.Select(u => $"• **{u.IngameName}**"));

            await message.Channel.SendMessageAsync(embed:
                EmbedHelper.Warning(
                    $"❌ **Unpaid Members (This Week)**\n\n{list}"
                ));
        }

        // ===================================================================
        // !CHECKBANK
        // ===================================================================
        private async Task CheckUserDonation(SocketMessage message)
        {
            string discordId = message.Author.Id.ToString();
            var member = await _memberService.GetMemberByDiscordIdAsync(discordId);

            if (member == null)
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error("You are not registered. Use `!register`."));
                return;
            }

            if (member.IsExempt)
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Info("Bank Status", "🟦 You are exempt from this week's donation."));
                return;
            }

            int total = await _donationService.GetTotalForUserThisWeekAsync(discordId);

            if (total > 0)
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Success("🎉 You have paid your weekly donation!"));
            }
            else
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error(
                        $"You have **NOT** paid your weekly donation.\nUpload your screenshot in <#{DonationChannelId}>."
                    ));
            }
        }

        // ===================================================================
        // !BANKREMINDER
        // ===================================================================
        private async Task SendBankReminder(SocketMessage message)
        {
            if (!IsOfficer(message)) return;

            await message.Channel.SendMessageAsync(embed:
                EmbedHelper.Info("Bank Reminder", "🏦 Sending bank reminders in the background…"));

            _ = Task.Run(async () =>
            {
                if (message.Channel is SocketGuildChannel gc)
                    await SendBankReminderBackground(gc);
            });
        }

        private async Task SendBankReminderBackground(SocketGuildChannel guildChannel)
        {
            var guild = guildChannel.Guild;
            var members = await _memberService.GetAllMembersAsync();
            var totals = await _donationService.GetTotalsForAllUsersThisWeekAsync();

            var unpaid = members
                .Where(m => !m.IsExempt &&
                            (!totals.ContainsKey(m.DiscordUserId) ||
                             totals[m.DiscordUserId] <= 0))
                .ToList();

            int sent = 0;
            List<ulong> failed = new();

            foreach (var m in unpaid)
            {
                ulong userId = ulong.Parse(m.DiscordUserId);
                var user = guild.GetUser(userId);

                if (user == null)
                {
                    failed.Add(userId);
                    continue;
                }

                try
                {
                    var dm = await user.CreateDMChannelAsync();

                    await dm.SendMessageAsync(
                        $"🏦 Hello **{m.IngameName}**, this is your weekly bank donation reminder.\n" +
                        $"Donate in <#{DonationChannelId}>.\n" +
                        $"You have till 18UTC the day of the reset change. Fines get rolled out around this time."
                    );

                    sent++;
                    await Task.Delay(1200);
                }
                catch
                {
                    failed.Add(userId);

                    await LogOfficer("Bank Reminder DM Failure", new()
                    {
                        { "UserId", user.Id.ToString()},
                        { "UserName", user.DisplayName }
                    });
                }
            }

            var channel = guildChannel as IMessageChannel;

            if (channel != null)
            {
                await channel.SendMessageAsync(embed:
                    EmbedHelper.Info("📩 Bank Reminder Summary",
                        $"• Unpaid: **{unpaid.Count}**\n" +
                        $"• DMs Sent: **{sent}**\n" +
                        $"• Failed: **{failed.Count}**"));
            }
        }

        // ===================================================================
        // !PAYFOR
        // ===================================================================
        private async Task HandlePayFor(SocketMessage message)
        {
            ulong userId = message.Author.Id;
            string args = message.Content.Substring("!payfor".Length).Trim();

            if (message.Channel.Id != DonationChannelId &&
                message.Channel.Id != FinePaymentChannelId)
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error("`!payfor` can only be used in donation or fine payment channels."));
                return;
            }

            if (string.IsNullOrWhiteSpace(args))
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Warning("Usage:\n`!payfor <IngameName>`\n`!payfor @DiscordUser`"));
                return;
            }

            // Payfor @mention
            if (message.MentionedUsers.Any())
            {
                var target = message.MentionedUsers.First();
                string targetId = target.Id.ToString();

                var member = await _memberService.GetMemberByDiscordIdAsync(targetId);
                if (member == null)
                {
                    await message.Channel.SendMessageAsync(embed:
                        EmbedHelper.Error($"{target.Username} is not registered."));
                    return;
                }

                _payForOverride[userId] = $"DISCORD:{targetId}";

                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Success($"Your next upload will count for **{target.Username}**."));
                return;
            }

            // Payfor by name
            var allMembers = await _memberService.GetAllMembersAsync();
            var matches = allMembers
                .Where(m => m.IngameName.Equals(args, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (!matches.Any())
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error($"No registered member found named **{args}**."));
                return;
            }

            if (matches.Count > 1)
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Warning($"Multiple members found with name **{args}**. Use @mention instead."));
                return;
            }

            _payForOverride[userId] = $"NAME:{args}";

            await message.Channel.SendMessageAsync(embed:
                EmbedHelper.Success($"Your next upload will count for **{args}**."));
        }


        // ===================================================================
        // Logging for unpaid bank members
        // ===================================================================

        public async Task LogUnpaidBeforeResetAsync()
        {
            var members = await _memberService.GetAllMembersAsync();
            var totals = await _donationService.GetTotalsForAllUsersThisWeekAsync();

            var unpaid = members
                .Where(m => !m.IsExempt &&
                            (!totals.ContainsKey(m.DiscordUserId) ||
                             totals[m.DiscordUserId] <= 0))
                .ToList();

            string message;

            if (!unpaid.Any())
            {
                message = "🎉 **Weekly Bank Audit (Pre-Reset)**\n\nAll members have paid or are exempt.";
            }
            else
            {
                string list = string.Join("\n", unpaid.Select(m =>
                    $"• **{m.IngameName}** ({m.IngameId}, {m.DiscordUserId})"));

                message =
                    "📋 **Weekly Bank Audit (Pre-Reset)**\n\n" +
                    "**Unpaid Members:**\n" +
                    list;
            }

            var channel = _client.GetChannel(OfficerLogChannelId) as IMessageChannel;

            if (channel != null)
            {
                await channel.SendMessageAsync(embed: EmbedHelper.Warning(message));
            }
        }

        // ===================================================================
        // Test command bank fine command (Unpaid version)
        // ===================================================================
        private async Task HandleBankFineCommand(SocketMessage message)
        {
            if (!IsOfficer(message)) return;

            var parts = message.Content
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);

            await HandleBankFineLive(message);
        }

        private async Task HandleBankFineLive(SocketMessage message)
        {
            await message.Channel.SendMessageAsync(embed:
                EmbedHelper.Info(
                    "Bank Fine Processing",
                    "💸 Issuing bank fines to all unpaid members…"
                ));

            var members = await _memberService.GetAllMembersAsync();
            var totals = await _donationService.GetTotalsForAllUsersThisWeekAsync();

            var unpaid = members
                .Where(m => !m.IsExempt &&
                    (!totals.ContainsKey(m.DiscordUserId) ||
                     totals[m.DiscordUserId] <= 0))
                .ToList();

            if (!unpaid.Any())
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Success("🎉 No unpaid members found."));
                return;
            }

            var now = DateTime.UtcNow;
            int daysSinceMonday = ((int)now.DayOfWeek + 6) % 7;
            DateTime weekStart = now.Date.AddDays(-daysSinceMonday);

            var guild = _client.GetGuild(GuildId);

            int fined = 0;
            List<string> fineFailures = new();
            List<string> dmFailures = new();

            foreach (var member in unpaid)
            {
                try
                {
                    if (await _fineService.AddBankFineAsync(
                            member,
                            BankFineAmount,
                            weekStart,
                            "Missed weekly bank donation"))
                    {
                        fined++;
                    }
                }
                catch
                {
                    fineFailures.Add($"{member.IngameName} ({member.DiscordUserId})");
                    continue;
                }

                try
                {
                    if (guild != null &&
                        ulong.TryParse(member.DiscordUserId, out ulong uid))
                    {
                        var user = guild.GetUser(uid);
                        if (user != null)
                        {
                            var dm = await user.CreateDMChannelAsync();
                            await dm.SendMessageAsync(
                                $"💸 **Bank Fine Issued**\n\n" +
                                $"You did not pay your weekly bank donation.\n" +
                                $"Fine Amount: **{BankFineAmount:N0}**\n\n" +
                                $"Please pay in <#{FinePaymentChannelId}>.\nYou have **3 days** to complete payment."
                            );
                        }
                        else
                        {
                            dmFailures.Add($"{member.IngameName} — user not found");
                        }
                    }
                }
                catch
                {
                    dmFailures.Add($"{member.IngameName} — DM failed");
                }
            }

            var fields = new Dictionary<string, string>
              {
                  { "Mode", "LIVE" },
                  { "Unpaid Members", unpaid.Count.ToString() },
                  { "Fines Issued", fined.ToString() },
                  { "Amount", BankFineAmount.ToString("N0") }
              };

            if (fineFailures.Any())
                fields["Fine Failures"] = string.Join("\n", fineFailures);

            if (dmFailures.Any())
                fields["DM Failures"] = string.Join("\n", dmFailures);

            await LogOfficer("Bank Fine Execution", fields);

            await message.Channel.SendMessageAsync(embed:
                EmbedHelper.Success(
                    $"💸 **Bank Fines Completed**\n\n" +
                    $"• Unpaid members: **{unpaid.Count}**\n" +
                    $"• Successfully fined: **{fined}**\n" +
                    $"• Fine amount: **{BankFineAmount:N0}**\n" +
                    $"• Fine failures: **{fineFailures.Count}**\n" +
                    $"• DM failures: **{dmFailures.Count}**"
                ));
        }





        // ===================================================================
        // OCR DONATION HANDLING
        // ===================================================================
        private async Task HandleDonationUpload(SocketMessage message)
        {
            string uploaderId = message.Author.Id.ToString();

            string targetDiscordId = uploaderId;
            string? targetNameOverride = null;

            if (_payForOverride.TryGetValue(message.Author.Id, out string overrideValue))
            {
                if (overrideValue.StartsWith("DISCORD:"))
                    targetDiscordId = overrideValue["DISCORD:".Length..];
                else if (overrideValue.StartsWith("NAME:"))
                    targetNameOverride = overrideValue["NAME:".Length..];

                _payForOverride.Remove(message.Author.Id);
            }

            Member? targetMember;

            if (targetNameOverride != null)
            {
                var all = await _memberService.GetAllMembersAsync();
                targetMember = all.FirstOrDefault(m =>
                    m.IngameName.Equals(targetNameOverride, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                targetMember = await _memberService.GetMemberByDiscordIdAsync(targetDiscordId);
            }

            if (targetMember == null)
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error("Target member was not found."));
                return;
            }

            int total = 0;

            foreach (var attachment in message.Attachments)
            {
                if (attachment.ContentType == null ||
                    !attachment.ContentType.StartsWith("image"))
                    continue;

                string tmp = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.png");

                using (var client = new HttpClient())
                {
                    var data = await client.GetByteArrayAsync(attachment.Url);
                    await File.WriteAllBytesAsync(tmp, data);
                }

                int? amount = await _ocrService.ExtractDonationAmountAsync(tmp);
                File.Delete(tmp);

                if (amount.HasValue)
                    total += amount.Value;
            }

            if (total <= 0)
            {
                await message.AddReactionAsync(new Emoji("❌"));
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error("I could not read any donation amount."));
                return;
            }

            // Save donation
            var now = DateTime.UtcNow;
            int daysSinceMonday = ((int)now.DayOfWeek + 6) % 7;
            DateTime weekStart = now.Date.AddDays(-daysSinceMonday);
            DateTime weekEnd = weekStart.AddDays(7);

            await _donationService.AddDonationAsync(new DonationRecord
            {
                DiscordUserId = targetMember.DiscordUserId,
                IngameName = targetMember.IngameName,
                Amount = total,
                TimestampUtc = now,
                WeekStartUtc = weekStart,
                WeekEndUtc = weekEnd
            });

            await message.AddReactionAsync(new Emoji("✅"));

            await message.Channel.SendMessageAsync(embed:
                EmbedHelper.Success("Your donation has been recorded."));
        }

        // ===================================================================
        // HELPERS
        // ===================================================================
        private bool IsOfficer(SocketMessage message)
        {
            if (message.Channel is not SocketGuildChannel)
            {
                _ = message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error("This command must be used inside the server."));
                return false;
            }

            var user = (message.Channel as SocketGuildChannel)!.GetUser(message.Author.Id);

            if (user == null || !user.Roles.Any(r => r.Id == OfficerRoleId))
            {
                _ = message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error($"{message.Author.Mention}, you do not have permission."));
                return false;
            }

            return true;
        }
    }
}
