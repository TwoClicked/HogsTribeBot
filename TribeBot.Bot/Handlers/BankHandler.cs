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

        // ===================================================================
        // EMBED HELPERS (local to this handler)
        // ===================================================================

        private Embed BuildEmbed(string title, string desc, Color color)
        {
            return new EmbedBuilder()
                .WithTitle(title)
                .WithDescription(desc)
                .WithColor(color)
                .WithFooter($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC")
                .Build();
        }

        private Task SendSuccess(SocketMessage msg, string text)
            => msg.Channel.SendMessageAsync(embed: BuildEmbed("🟢 Success", text, Color.Green));

        private Task SendError(SocketMessage msg, string text)
            => msg.Channel.SendMessageAsync(embed: BuildEmbed("❌ Error", text, Color.Red));

        private Task SendWarning(SocketMessage msg, string text)
            => msg.Channel.SendMessageAsync(embed: BuildEmbed("⚠️ Warning", text, Color.Orange));

        private Task SendInfo(SocketMessage msg, string title, string text)
            => msg.Channel.SendMessageAsync(embed: BuildEmbed($"🛡️ {title}", text, Color.Blue));

        private IMessageChannel OfficerLog =>
            _client.GetChannel(OfficerLogChannelId) as IMessageChannel;

        private Task SendLog(string title, Dictionary<string, string> fields)
        {
            var eb = new EmbedBuilder()
                .WithTitle($"📘 {title}")
                .WithColor(new Color(0, 110, 255))
                .WithFooter($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");

            foreach (var kvp in fields)
                eb.AddField(kvp.Key, kvp.Value, true);

            return OfficerLog?.SendMessageAsync(embed: eb.Build()) ?? Task.CompletedTask;
        }

        // ===================================================================
        // ROOT ENTRY
        // ===================================================================
        public async Task<bool> TryHandleAsync(SocketMessage message)
        {
            if (message.Author.IsBot)
                return false;

            string content = message.Content.Trim().ToLower();

            if (content.Equals("!bankunpaid"))
            {
                await ShowUnpaid(message);
                return true;
            }

            if (content.Equals("!checkbank"))
            {
                await CheckUserDonation(message);
                return true;
            }

            if (content.Equals("!bankreminder"))
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

            if (unpaid.Count == 0)
            {
                await SendSuccess(message, "🎉 Everyone has paid or is exempt!");
                return;
            }

            string list = string.Join("\n", unpaid.Select(u => $"• **{u.IngameName}**"));

            await SendWarning(
                message,
                $"❌ **Unpaid Members (This Week)**\n\n{list}"
            );
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
                await SendError(message, "You are not registered. Use `!register`.");
                return;
            }

            if (member.IsExempt)
            {
                await SendInfo(message, "Bank Status", "🟦 You are exempt from this week's donation.");
                return;
            }

            int total = await _donationService.GetTotalForUserThisWeekAsync(discordId);

            if (total > 0)
            {
                await SendSuccess(message, "🎉 You have paid your weekly donation!");
            }
            else
            {
                await SendError(
                    message,
                    $"You have **NOT** paid your weekly donation.\nPlease upload your screenshot in <#{DonationChannelId}>."
                );
            }
        }

        // ===================================================================
        // !BANKREMINDER
        // ===================================================================
        private async Task SendBankReminder(SocketMessage message)
        {
            if (!IsOfficer(message))
                return;

            await SendInfo(message, "Bank Reminder", "🏦 Sending bank reminders in the background…");

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
                        $"Your donation is required in <#{DonationChannelId}>.\n" +
                        $"You have until 1 hour before weekly reset — failure may result in removal."
                    );

                    sent++;
                    await Task.Delay(1200);
                }
                catch
                {
                    failed.Add(userId);

                    await SendLog("Bank Reminder DM Failure", new()
                    {
                        { "User", userId.ToString() }
                    });
                }
            }

            var channel = guildChannel as IMessageChannel;

            if (channel != null)
            {
                await channel.SendMessageAsync(
                    embed: BuildEmbed(
                        "📩 Bank Reminder Summary",
                        $"• Unpaid: **{unpaid.Count}**\n" +
                        $"• DMs Sent: **{sent}**\n" +
                        $"• Failed: **{failed.Count}**",
                        Color.Blue
                    )
                );
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
                await SendError(message, "`!payfor` can only be used in donation or fine payment channels.");
                return;
            }

            if (string.IsNullOrWhiteSpace(args))
            {
                await SendWarning(message, "Usage:\n`!payfor <IngameName>`\n`!payfor @DiscordUser`");
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
                    await SendError(message, $"{target.Username} is not registered.");
                    return;
                }

                _payForOverride[userId] = $"DISCORD:{targetId}";

                await SendSuccess(message, $"Your next upload will count for **{target.Username}**.");
                return;
            }

            // Payfor by ingame name
            var allMembers = await _memberService.GetAllMembersAsync();
            var matches = allMembers
                .Where(m => m.IngameName.Equals(args, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matches.Count == 0)
            {
                await SendError(message, $"No registered member found named **{args}**.");
                return;
            }

            if (matches.Count > 1)
            {
                await SendWarning(message, $"Multiple members found with name **{args}**. Use @mention instead.");
                return;
            }

            _payForOverride[userId] = $"NAME:{args}";

            await SendSuccess(message, $"Your next upload will count for **{args}**.");
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
                await SendError(message, "Target member was not found.");
                return;
            }

            int total = 0;

            foreach (var attachment in message.Attachments)
            {
                if (attachment.ContentType == null ||
                    !attachment.ContentType.StartsWith("image"))
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
                await SendError(message, "I could not read any donation amount.");
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
            await SendSuccess(message, "Your donation has been recorded.");
        }

        // ===================================================================
        // HELPERS
        // ===================================================================
        private bool IsOfficer(SocketMessage message)
        {
            if (message.Channel is not SocketGuildChannel gc)
            {
                _ = SendError(message, "This command must be used inside the server.");
                return false;
            }

            var user = gc.GetUser(message.Author.Id);

            if (user == null || !user.Roles.Any(r => r.Id == OfficerRoleId))
            {
                _ = SendError(message, "You do not have permission to use this command.");
                return false;
            }

            return true;
        }
    }
}
