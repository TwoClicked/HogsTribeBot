using Discord;
using Discord.WebSocket;
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

        // PAYFOR override stored per user
        private readonly Dictionary<ulong, string> _payForOverride = new();

        // CONFIG: channel / role IDs
        private const ulong DonationChannelId = 1440050111353721053;
        private const ulong FinePaymentChannelId = 1440431172160061450;
        private const ulong OfficerRoleId = 1222665812775534592;
        private const ulong GuildId = 1109193500664287336;

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

        // ===================================================================
        // ROOT ENTRY — called from Program.cs
        // ===================================================================
        public async Task<bool> TryHandleAsync(SocketMessage message)
        {
            if (message.Author.IsBot)
                return false;

            string content = message.Content.Trim();

            // BANK COMMANDS

            if (content.Equals("!bankunpaid", StringComparison.OrdinalIgnoreCase))
            {
                await ShowUnpaid(message);
                return true;
            }

            if (content.Equals("!checkbank", StringComparison.OrdinalIgnoreCase))
            {
                await CheckUserDonation(message);
                return true;
            }

            if (content.Equals("!bankreminder", StringComparison.OrdinalIgnoreCase))
            {
                await SendBankReminder(message);
                return true;
            }

            if (content.StartsWith("!payfor", StringComparison.OrdinalIgnoreCase))
            {
                await HandlePayFor(message);
                return true;
            }

            // OCR donation handling (Donation channel only)
            if (message.Channel.Id == DonationChannelId && message.Attachments.Any())
            {
                await HandleDonationUpload(message);
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
                            (!totals.ContainsKey(m.DiscordUserId) || totals[m.DiscordUserId] <= 0))
                .ToList();

            if (unpaid.Count == 0)
            {
                await message.Channel.SendMessageAsync("🎉 Everyone has paid (or is exempt)!");
                return;
            }

            string msg = "❌ **Unpaid Members (This Week)**\n\n";
            foreach (var m in unpaid)
                msg += $"• **{m.IngameName}**\n";

            await message.Channel.SendMessageAsync(msg);
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
                await message.Channel.SendMessageAsync($"{message.Author.Mention} You are not registered. Use `!register`.");
                return;
            }

            if (member.IsExempt)
            {
                await message.Channel.SendMessageAsync($"{message.Author.Mention} 🟦 You are exempt from this week's donation.");
                return;
            }

            int total = await _donationService.GetTotalForUserThisWeekAsync(discordId);

            if (total > 0)
            {
                await message.Channel.SendMessageAsync(
                    $"{message.Author.Mention} 🎉 **You have paid your weekly donation!**");
            }
            else
            {
                await message.Channel.SendMessageAsync(
                    $"{message.Author.Mention} ❌ **You have NOT paid your weekly donation.**\n" +
                    $"Please upload your screenshot in <#{DonationChannelId}>.");
            }
        }

        // ===================================================================
        // !BANKREMINDER (OFFICER ONLY)
        // ===================================================================

        private async Task SendBankReminder(SocketMessage message)
        {
            if (!IsOfficer(message))
                return;

            if (message.Channel is not SocketGuildChannel guildChannel)
            {
                await message.Channel.SendMessageAsync("This command must be used inside the server.");
                return;
            }

            await message.Channel.SendMessageAsync("🏦 Sending bank reminders in the background…");

            _ = Task.Run(async () =>
            {
                await SendBankReminderBackground(guildChannel);
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

            var log = GetOfficerLog();

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
                        $"Your donation is required in <#{DonationChannelId}>.\n" +
                        $"You have untill 1 hour before the weekly reset, keep in mind, you will be removed if this is not brought in order.");

                    sent++;
                    await Task.Delay(1200);
                }
                catch
                {
                    failed.Add(userId);

                    if (log != null)
                        await log.SendMessageAsync($"⚠️ Could not DM <@{userId}>.");
                }
            }

            string result =
                $"📩 **Bank Reminder Summary**\n\n" +
                $"• Unpaid: **{unpaid.Count}**\n" +
                $"• DMs sent: **{sent}**\n" +
                $"• Failed: **{failed.Count}**";

            (await _client.GetChannelAsync(guildChannel.Id) as IMessageChannel)
                ?.SendMessageAsync(result);
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
                await message.Channel.SendMessageAsync("❌ `!payfor` can only be used in donation or fine payment channels.");
                return;
            }

            if (string.IsNullOrWhiteSpace(args))
            {
                await message.Channel.SendMessageAsync("Usage:\n`!payfor <IngameName>`\n`!payfor @DiscordUser`");
                return;
            }

            // CASE 1 — paying for @user
            if (message.MentionedUsers.Any())
            {
                var target = message.MentionedUsers.First();
                string targetId = target.Id.ToString();

                var member = await _memberService.GetMemberByDiscordIdAsync(targetId);
                if (member == null)
                {
                    await message.Channel.SendMessageAsync($"❌ <@{target.Id}> is not registered.");
                    return;
                }

                _payForOverride[userId] = $"DISCORD:{targetId}";
                await message.Channel.SendMessageAsync($"💰 Your next upload will count for **<@{target.Id}>**.");
                return;
            }

            // CASE 2 — paying for ingame name
            var allMembers = await _memberService.GetAllMembersAsync();
            var matches = allMembers
                .Where(m => m.IngameName.Equals(args, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matches.Count == 0)
            {
                await message.Channel.SendMessageAsync($"❌ No registered member found named **{args}**.");
                return;
            }

            if (matches.Count > 1)
            {
                await message.Channel.SendMessageAsync($"⚠ Multiple members named **{args}**. Use @mention instead.");
                return;
            }

            _payForOverride[userId] = $"NAME:{args}";
            await message.Channel.SendMessageAsync($"💰 Your next upload will count for **{args}**.");
        }


        // ===================================================================
        // OCR DONATION HANDLING
        // ===================================================================

        private async Task HandleDonationUpload(SocketMessage message)
        {
            string uploaderId = message.Author.Id.ToString();

            // Determine final donation target
            string targetDiscordId = uploaderId;
            string targetNameOverride = null;

            if (_payForOverride.TryGetValue(message.Author.Id, out string overrideValue))
            {
                if (overrideValue.StartsWith("DISCORD:"))
                    targetDiscordId = overrideValue.Substring("DISCORD:".Length);

                else if (overrideValue.StartsWith("NAME:"))
                    targetNameOverride = overrideValue.Substring("NAME:".Length);

                _payForOverride.Remove(message.Author.Id);
            }

            Member targetMember;
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
                await message.Channel.SendMessageAsync($"{message.Author.Mention} ❌ Target user not found.");
                return;
            }

            int total = 0;

            foreach (var attachment in message.Attachments)
            {
                if (attachment.ContentType == null || !attachment.ContentType.StartsWith("image"))
                    continue;

                string tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.png");

                using (var client = new HttpClient())
                {
                    var data = await client.GetByteArrayAsync(attachment.Url);
                    await File.WriteAllBytesAsync(tempFile, data);
                }

                int? amount = await _ocrService.ExtractDonationAmountAsync(tempFile);
                File.Delete(tempFile);

                if (amount.HasValue)
                    total += amount.Value;
            }

            if (total <= 0)
            {
                await message.AddReactionAsync(new Emoji("❌"));
                await message.Channel.SendMessageAsync($"{message.Author.Mention} I could not read any donation amount.");
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
            await message.Channel.SendMessageAsync(
                $"{message.Author.Mention} ** ✅ Your payment has been recorded.**");
        }

        // ===================================================================
        // HELPER: CHECK OFFICER ROLE
        // ===================================================================

        private bool IsOfficer(SocketMessage message)
        {
            if (message.Channel is not SocketGuildChannel gc)
            {
                message.Channel.SendMessageAsync("❌ You must use this inside the server.");
                return false;
            }

            var user = gc.GetUser(message.Author.Id);
            if (user == null || !user.Roles.Any(r => r.Id == OfficerRoleId))
            {
                message.Channel.SendMessageAsync($"{message.Author.Mention} ❌ You do not have permission.");
                return false;
            }

            return true;
        }

        private IMessageChannel GetOfficerLog()
        {
            return _client.GetChannel(1440209811621937273) as IMessageChannel;
        }
    }
}
