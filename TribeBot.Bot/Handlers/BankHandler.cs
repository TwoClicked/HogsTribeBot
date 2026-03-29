using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using TribeBot.Bot.UI;
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

        // In-memory weekly safeguard (no persistence by design)
        private DateTime? _lastAutoBankFineWeek;

        private readonly Dictionary<ulong, string> _payForOverride = new();

        private const ulong DonationChannelId = 1440050111353721053;
        private const ulong FinePaymentChannelId = 1440431172160061450;
        private const ulong OfficerRoleId = 1222665812775534592;
        private const ulong GuildId = 1109193500664287336;
        private const ulong OfficerLogChannelId = 1440211043820507217;

        private const int BankFineAmount = 75_000_000;

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

        private Task LogOfficer(string title, Dictionary<string, string> fields) =>
            OfficerLog?.SendMessageAsync(embed: EmbedHelper.Log(title, fields))
            ?? Task.CompletedTask;

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

            return false;
        }

        // ===================================================================
        // AUTOMATIC WEEKLY AUDIT + FINE (SUNDAY 18:00 UTC)
        // ===================================================================
        public async Task ExecuteWeeklyBankAuditAndFineAsync()
        {
            var now = DateTime.UtcNow;

            // Determine Monday week start
            int daysSinceMonday = ((int)now.DayOfWeek + 6) % 7;
            DateTime weekStart = now.Date.AddDays(-daysSinceMonday);

            // In-memory safeguard
            if (_lastAutoBankFineWeek == weekStart)
                return;

            _lastAutoBankFineWeek = weekStart;

            var members = await _memberService.GetAllMembersAsync();
            var totals = await _donationService.GetTotalsForAllUsersThisWeekAsync();

            var unpaid = members
                .Where(m => !m.BankExempt &&
                    (!totals.ContainsKey(m.DiscordUserId) ||
                     totals[m.DiscordUserId] <= 0))
                .ToList();

            await LogOfficer("Weekly Bank Audit (Auto)", new()
            {
                { "Week", weekStart.ToString("yyyy-MM-dd") },
                { "Unpaid Members", unpaid.Count.ToString() }
            });

            if (!unpaid.Any())
                return;

            var guild = _client.GetGuild(GuildId);

            int fined = 0;
            List<string> finedMembers = new();
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
                        finedMembers.Add($"{member.IngameName} ({member.DiscordUserId})");
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
                                $"💸 **Automatic Bank Fine Issued**\n\n" +
                                $"Remember Bank fine gets issued to everyone paying later then 18UTC on **SUNDAY**\n" +
                                $"You did not pay your weekly bank donation.\n" +
                                $"Fine Amount: **{BankFineAmount:N0}**\n\n" +
                                $"Please pay in <#{FinePaymentChannelId}>.\n" +
                                $"You have **3 days** to complete payment."
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
                { "Mode", "AUTO (Sunday 18:00 UTC)" },
                { "Unpaid Members", unpaid.Count.ToString() },
                { "Fines Issued", fined.ToString() },
                { "Amount", BankFineAmount.ToString("N0") }
            };

            if (finedMembers.Any())
            {
                fields["Fined Members"] = string.Join("\n", finedMembers);
            }

            if (fineFailures.Any())
                fields["Fine Failures"] = string.Join("\n", fineFailures);

            if (dmFailures.Any())
                fields["DM Failures"] = string.Join("\n", dmFailures);

            await LogOfficer("Weekly Bank Fines Issued", fields);
        }

        // ===================================================================
        // !BANKUNPAID
        // ===================================================================
        private async Task ShowUnpaid(SocketMessage message)
        {
            var members = await _memberService.GetAllMembersAsync();
            var totals = await _donationService.GetTotalsForAllUsersThisWeekAsync();

            var unpaid = members
                .Where(m => !m.BankExempt &&
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
                EmbedHelper.Warning($"❌ **Unpaid Members (This Week)**\n\n{list}"));
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

            if (member.BankExempt)
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
                        $"You have **NOT** paid your weekly donation.\n" +
                        $"Upload your screenshot in <#{DonationChannelId}>."
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
                .Where(m => !m.BankExempt &&
                    (!totals.ContainsKey(m.DiscordUserId) ||
                     totals[m.DiscordUserId] <= 0))
                .ToList();

            int sent = 0;
            List<ulong> failed = new();

            foreach (var m in unpaid)
            {
                if (!ulong.TryParse(m.DiscordUserId, out ulong uid))
                {
                    failed.Add(0);
                    continue;
                }

                var user = guild.GetUser(uid);
                if (user == null)
                {
                    failed.Add(uid);
                    continue;
                }

                try
                {
                    var dm = await user.CreateDMChannelAsync();
                    await dm.SendMessageAsync(
                        $"🏦 **Bank Donation Reminder**\n\n" +
                        $"Hello **{m.IngameName}**,\n" +
                        $"You have not paid your weekly bank donation.\n\n" +
                        $"Please donate in <#{DonationChannelId}>.\n" +
                        $"Fines are issued at **18:00 UTC on Sunday**."
                    );

                    sent++;
                    await Task.Delay(1200);
                }
                catch
                {
                    failed.Add(uid);
                }
            }

            if (guildChannel is IMessageChannel channel)
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

            if (message.MentionedUsers.Any())
            {
                var target = message.MentionedUsers.First();
                var member = await _memberService.GetMemberByDiscordIdAsync(target.Id.ToString());

                if (member == null)
                {
                    await message.Channel.SendMessageAsync(embed:
                        EmbedHelper.Error($"{target.Username} is not registered."));
                    return;
                }

                _payForOverride[userId] = $"DISCORD:{target.Id}";
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Success($"Your next upload will count for **{target.Username}**."));
                return;
            }

            var allMembers = await _memberService.GetAllMembersAsync();
            var match = allMembers.FirstOrDefault(m =>
                m.IngameName.Equals(args, StringComparison.OrdinalIgnoreCase));

            if (match == null)
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error($"No registered member found named **{args}**."));
                return;
            }

            _payForOverride[userId] = $"NAME:{args}";
            await message.Channel.SendMessageAsync(embed:
                EmbedHelper.Success($"Your next upload will count for **{args}**."));
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

            var now = DateTime.UtcNow;
            int daysSinceMonday = ((int)now.DayOfWeek + 6) % 7;
            DateTime weekStart = now.Date.AddDays(-daysSinceMonday);
            DateTime weekEnd = weekStart.AddDays(7);

            var detectedDate = _ocrService.LastDetectedDonationDateUtc;

            if (detectedDate != null)
            {
                if (detectedDate < weekStart || detectedDate >= weekEnd)
                {
                    await OfficerLog.SendMessageAsync(embed:
                        EmbedHelper.Warning(
                            $"⚠️ **Donation Outside Current Week**\n\n" +
                            $"Member: **{targetMember.IngameName}**\n" +
                            $"Detected Date: {detectedDate:yyyy-MM-dd}\n" +
                            $"Week Range: {weekStart:yyyy-MM-dd} → {weekEnd:yyyy-MM-dd}"
                        ));
                }
            }
            else
            {
                await OfficerLog.SendMessageAsync(embed:
                    EmbedHelper.Warning(
                        $"⚠️ **Donation Without Detectable Date**\n\n" +
                        $"Member: **{targetMember.IngameName}**\n" +
                        "OCR could not detect a transaction date."
                    ));
            }



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

            var user = ((SocketGuildChannel)message.Channel).GetUser(message.Author.Id);

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
