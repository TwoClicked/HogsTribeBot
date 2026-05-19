using Discord;
using Discord.WebSocket;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using TribeBot.Bot.UI;
using TribeBot.Core.Entities;
using TribeBot.Core.Interfaces;
using TribeBot.Services.Services;
using Img = SixLabors.ImageSharp.Image;
using ImgRect = SixLabors.ImageSharp.Rectangle;
using Rgba32 = SixLabors.ImageSharp.PixelFormats.Rgba32;

namespace TribeBot.Bot.Handlers
{
    public class DeliveryHandler
    {
        private readonly DiscordSocketClient _client;
        private readonly IMemberService _memberService;
        private readonly IDeliveryEventService _deliveryService;
        private readonly PaddleOcrServerService _ocrService;

        private const ulong DeliveryChannelId = 1487724856735957042;
        private const ulong OfficerRoleId = 1222665812775534592;
        private const ulong OfficerLogChannelId = 1440211043820507217;
        private const ulong GuildId = 1109193500664287336;
        private const ulong FinePaymentChannelId = 1440431172160061450;

        public const int BraceletRequirement = 1500;
        public const long GoldRequirement = 75_000_000;
        public const long FineAmount = 200_000_000;

        // Tracks which mode (gold/bracelet) each user has selected
        private readonly Dictionary<ulong, string> _submissionMode = new();

        // Tracks "donate for someone else" overrides, keyed by uploader's Discord ID
        private readonly Dictionary<ulong, string> _donateForOverride = new();

        public DeliveryHandler(
            DiscordSocketClient client,
            IMemberService memberService,
            IDeliveryEventService deliveryService,
            PaddleOcrServerService ocrService)
        {
            _client = client;
            _memberService = memberService;
            _deliveryService = deliveryService;
            _ocrService = ocrService;
        }

        private IMessageChannel OfficerLog =>
            _client.GetChannel(OfficerLogChannelId) as IMessageChannel;

        // ============================================================
        // ENTRY POINT
        // ============================================================
        public async Task<bool> TryHandleAsync(SocketMessage message)
        {
            if (message.Author.IsBot)
                return false;

            string content = message.Content.Trim().ToLower();

            if (content == "!deliverystart")
            {
                await StartEvent(message);
                return true;
            }

            if (content == "!deliveryend")
            {
                await EndEvent(message);
                return true;
            }

            if (content == "!deliverystatus")
            {
                await ShowStatus(message);
                return true;
            }

            if (content == "!checkdelivery")
            {
                await CheckUserDeliveryStatus(message);
                return true;
            }

            if (content == "!deliveryreminder")
            {
                await SendDeliveryReminder(message);
                return true;
            }

            if (content.StartsWith("!donatefor"))
            {
                await HandleDonateFor(message);
                return true;
            }

            if (content == "!gold" || content == "gold")
            {
                var eventId = await _deliveryService.GetActiveEventIdAsync();

                if (eventId == null)
                {
                    await message.Channel.SendMessageAsync(embed:
                        EmbedHelper.Warning(
                            "❌ No active Sworn Vengeance event.\n\n" +
                            "Please wait until a Delivery Event starts."
                        ));
                    return true;
                }

                _submissionMode[message.Author.Id] = "gold";

                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Info(
                        "Gold Submission",
                        $"💰 Upload your screenshot now.\n\nRequirement: **≥ {GoldRequirement} gold**"
                    ));

                return true;
            }

            if (content == "!bracelet" || content == "bracelet")
            {
                var eventId = await _deliveryService.GetActiveEventIdAsync();

                if (eventId == null)
                {
                    await message.Channel.SendMessageAsync(embed:
                        EmbedHelper.Warning(
                            "❌ No active Sworn Vengeance event.\n\n" +
                            "Please wait until a Delivery Event starts."
                        ));
                    return true;
                }

                _submissionMode[message.Author.Id] = "bracelet";

                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Info(
                        "Bracelet Submission",
                        $"📿 Upload your screenshot now.\n\nRequirement: **≥ {BraceletRequirement} bracelets**"
                    ));

                return true;
            }

            if (message.Channel.Id == DeliveryChannelId && message.Attachments.Any())
            {
                await HandleSubmission(message);
                return true;
            }

            return false;
        }

        // ============================================================
        // START EVENT
        // ============================================================
        private async Task StartEvent(SocketMessage message)
        {
            if (!IsOfficer(message)) return;

            var eventId = await _deliveryService.StartEventAsync();

            await message.Channel.SendMessageAsync(embed:
                EmbedHelper.Success(
                    $"🚀 **Delivery Event Started**\n\n" +
                    $"For Delivery you have two options:\n" +
                    $"📿 ≥ {BraceletRequirement} bracelets\n" +
                    $"💰 ≥ {GoldRequirement} gold\n\n" +
                    $"Submit in <#{DeliveryChannelId}>"
                ));

            await OfficerLog?.SendMessageAsync(embed:
                EmbedHelper.Log("Delivery Event Started", new()
                {
                    { "EventId", eventId },
                    { "Started By", message.Author.Username }
                }));

            // Fire DMs to all members in the background
            _ = Task.Run(async () =>
            {
                if (message.Channel is SocketGuildChannel gc)
                    await SendStartNotificationsBackground(gc);
            });
        }

        private async Task SendStartNotificationsBackground(SocketGuildChannel guildChannel)
        {
            var guild = guildChannel.Guild;
            var allMembers = await _memberService.GetAllMembersAsync();

            // Fetch ALL guild members from the API, not just the cache
            var guildUsers = (await guild.GetUsersAsync().FlattenAsync())
                .ToDictionary(u => u.Id);

            int sent = 0;
            List<string> failures = new();

            foreach (var member in allMembers)
            {
                if (!ulong.TryParse(member.DiscordUserId, out ulong uid))
                {
                    failures.Add($"{member.IngameName} — invalid Discord ID");
                    continue;
                }

                if (!guildUsers.TryGetValue(uid, out var user))
                {
                    failures.Add($"{member.IngameName} — user not found");
                    continue;
                }

                try
                {
                    var dm = await user.CreateDMChannelAsync();
                    await dm.SendMessageAsync(
                        $"📦 **Delivery Event Started**\n\n" +
                        $"Hello **{member.IngameName}**,\n\n" +
                        $"A new delivery event has begun!\n\n" +
                        $"Options:\n" +
                        $"📿 ≥ {BraceletRequirement} bracelets\n" +
                        $"💰 ≥ {GoldRequirement} gold\n\n" +
                        $"Submit your screenshot in <#{DeliveryChannelId}>.\n" +
                        $"Type **bracelet** or **gold** first, then upload."
                    );

                    sent++;
                    await Task.Delay(1200);
                }
                catch
                {
                    failures.Add($"{member.IngameName} — DM failed");
                }
            }
        }


        // ============================================================
        // END EVENT
        // ============================================================
        private async Task EndEvent(SocketMessage message)
        {
            if (!IsOfficer(message)) return;

            var eventId = await _deliveryService.GetActiveEventIdAsync();
            if (eventId == null)
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error("No active delivery event."));
                return;
            }

            var finedMembers = await _deliveryService.EndEventAsync(eventId);

            var guild = _client.GetGuild(GuildId);

            int dmSent = 0;
            List<string> dmFailures = new();
            List<string> finedList = new();

            foreach (var member in finedMembers)
            {
                finedList.Add($"{member.IngameName} ({member.DiscordUserId})");

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
                                $"💸 **Delivery Event Fine Issued**\n\n" +
                                $"You did not complete the delivery event.\n\n" +
                                $"Fine Amount: **{FineAmount} gold**\n\n" +
                                $"Please pay in <#{FinePaymentChannelId}>.\n" +
                                $"If you believe this is incorrect, contact an officer."
                            );

                            dmSent++;
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
                { "Mode",         "Manual Event End" },
                { "EventId",      eventId },
                { "Fines Issued", finedMembers.Count.ToString() },
                { "Amount",       FineAmount.ToString("N0") },
                { "DM Sent",      dmSent.ToString() }
            };

            if (finedList.Any())
                fields["Fined Members"] = string.Join("\n", finedList);

            if (dmFailures.Any())
                fields["DM Failures"] = string.Join("\n", dmFailures);

            await OfficerLog?.SendMessageAsync(embed:
                EmbedHelper.Log("Delivery Event Fines Issued", fields));

            await message.Channel.SendMessageAsync(embed:
                EmbedHelper.Warning(
                    $"❌ **Delivery Event Ended**\n\n" +
                    $"Fines Issued: **{finedMembers.Count}**\n" +
                    $"DM Sent: **{dmSent}**\n" +
                    $"Amount: **{FineAmount:N0}**"
                ));
        }

        // ============================================================
        // USER STATUS CHECK
        // ============================================================
        private async Task CheckUserDeliveryStatus(SocketMessage message)
        {
            string discordId = message.Author.Id.ToString();

            var member = await _memberService.GetMemberByDiscordIdAsync(discordId);

            if (member == null)
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error("You are not registered. Use `!register`."));
                return;
            }

            var eventId = await _deliveryService.GetActiveEventIdAsync();

            if (eventId == null)
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Info("Delivery Status", "No active delivery event."));
                return;
            }

            bool completed = await _deliveryService.HasUserParticipatedAsync(eventId, discordId);

            if (completed)
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Success("✅ You have completed the delivery event."));
            }
            else
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Warning(
                        $"❌ You have NOT completed the delivery event.\n" +
                        $"Go to <#{DeliveryChannelId}> and submit your proof."
                    ));
            }
        }

        // ============================================================
        // STATUS (OFFICER)
        // ============================================================
        private async Task ShowStatus(SocketMessage message)
        {
            if (!IsOfficer(message)) return;

            var eventId = await _deliveryService.GetActiveEventIdAsync();
            if (eventId == null)
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error("No active delivery event."));
                return;
            }

            var missing = await _deliveryService.GetNonParticipantsAsync(eventId);

            if (!missing.Any())
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Success("✅ Everyone has completed the delivery event!"));
                return;
            }

            string list = string.Join("\n", missing.Select(x => $"• **{x}**"));

            await message.Channel.SendMessageAsync(embed:
                EmbedHelper.Warning(
                    $"❌ **Missing Players ({missing.Count})**\n\n{list}"
                ));
        }

        // ============================================================
        // !DELIVERYREMINDER  (officer-only)
        // ============================================================
        private async Task SendDeliveryReminder(SocketMessage message)
        {
            if (!IsOfficer(message)) return;

            var eventId = await _deliveryService.GetActiveEventIdAsync();
            if (eventId == null)
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error("No active delivery event."));
                return;
            }

            await message.Channel.SendMessageAsync(embed:
                EmbedHelper.Info("Delivery Reminder", "📬 Sending reminders in the background…"));

            _ = Task.Run(async () =>
            {
                if (message.Channel is SocketGuildChannel gc)
                    await SendDeliveryReminderBackground(gc, eventId);
            });
        }

        private async Task SendDeliveryReminderBackground(SocketGuildChannel guildChannel, string eventId)
        {
            var guild = guildChannel.Guild;
            var missing = await _deliveryService.GetNonParticipantsAsync(eventId);

            var allMembers = await _memberService.GetAllMembersAsync();
            var targets = allMembers
                .Where(m => missing.Contains(m.IngameName, StringComparer.OrdinalIgnoreCase))
                .ToList();

            int sent = 0;
            List<string> failures = new();

            foreach (var member in targets)
            {
                if (!ulong.TryParse(member.DiscordUserId, out ulong uid))
                {
                    failures.Add($"{member.IngameName} — invalid Discord ID");
                    continue;
                }

                var user = guild.GetUser(uid);
                if (user == null)
                {
                    failures.Add($"{member.IngameName} — user not found");
                    continue;
                }

                try
                {
                    var dm = await user.CreateDMChannelAsync();
                    await dm.SendMessageAsync(
                        $"📦 **Delivery Event Reminder**\n\n" +
                        $"Hello **{member.IngameName}**,\n\n" +
                        $"You have not yet completed the active delivery event.\n\n" +
                        $"Options:\n" +
                        $"📿 ≥ {BraceletRequirement} bracelets\n" +
                        $"💰 ≥ {GoldRequirement} gold\n\n" +    
                        $"Submit your screenshot in <#{DeliveryChannelId}>.\n" +
                        $"Type **bracelet** or **gold** first, then upload."
                    );

                    sent++;
                    await Task.Delay(1200); // avoid Discord rate limits
                }
                catch
                {
                    failures.Add($"{member.IngameName} — DM failed");
                }
            }

            if (guildChannel is IMessageChannel channel)
            {
                await channel.SendMessageAsync(embed:
                    EmbedHelper.Info("📬 Reminder Summary",
                        $"• Missing: **{targets.Count}**\n" +
                        $"• DMs Sent: **{sent}**\n" +
                        $"• Failed: **{failures.Count}**"
                    ));
            }

            if (failures.Any())
            {
                await OfficerLog?.SendMessageAsync(embed:
                    EmbedHelper.Warning(
                        $"⚠️ **Delivery Reminder Failures**\n\n" +
                        string.Join("\n", failures)
                    ));
            }
        }

        // ============================================================
        // !DONATEFOR
        // ============================================================
        private async Task HandleDonateFor(SocketMessage message)
        {
            if (message.Channel.Id != DeliveryChannelId)
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error($"`!donatefor` can only be used in <#{DeliveryChannelId}>."));
                return;
            }

            string args = message.Content.Substring("!donatefor".Length).Trim();

            if (string.IsNullOrWhiteSpace(args))
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Warning(
                        "Usage:\n`!donatefor <IngameName>`\n`!donatefor @DiscordUser`"
                    ));
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

                _donateForOverride[message.Author.Id] = $"DISCORD:{target.Id}";

                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Success(
                        $"Your next submission will count for **{member.IngameName}**.\n" +
                        $"Type **bracelet** or **gold** and then upload your screenshot."
                    ));
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

            _donateForOverride[message.Author.Id] = $"NAME:{args}";

            await message.Channel.SendMessageAsync(embed:
                EmbedHelper.Success(
                    $"Your next submission will count for **{match.IngameName}**.\n" +
                    $"Type **bracelet** or **gold** and then upload your screenshot."
                ));
        }

        // ============================================================
        // HANDLE SUBMISSION
        // ============================================================
        private async Task HandleSubmission(SocketMessage message)
        {
            var eventId = await _deliveryService.GetActiveEventIdAsync();

            if (eventId == null)
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Warning(
                        "❌ No active Delivery Event.\n\n" +
                        "Submissions are currently closed."
                    ));
                return;
            }

            // ── Resolve uploader ──────────────────────────────────────
            string uploaderDiscordId = message.Author.Id.ToString();

            string? targetDiscordId = uploaderDiscordId;
            string? targetNameOverride = null;

            if (_donateForOverride.TryGetValue(message.Author.Id, out string? overrideValue))
            {
                if (overrideValue.StartsWith("DISCORD:"))
                    targetDiscordId = overrideValue["DISCORD:".Length..];
                else if (overrideValue.StartsWith("NAME:"))
                {
                    targetNameOverride = overrideValue["NAME:".Length..];
                    targetDiscordId = null;
                }

                _donateForOverride.Remove(message.Author.Id);
            }

            Member? member;

            if (targetNameOverride != null)
            {
                var all = await _memberService.GetAllMembersAsync();
                member = all.FirstOrDefault(m =>
                    m.IngameName.Equals(targetNameOverride, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                member = await _memberService.GetMemberByDiscordIdAsync(targetDiscordId!);
            }

            if (member == null)
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error("Target member was not found or is not registered."));
                return;
            }

            // ── Mode check ────────────────────────────────────────────
            if (!_submissionMode.TryGetValue(message.Author.Id, out string mode))
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Warning("Use **gold** or **bracelet** first, then upload."));
                return;
            }

            // ── OCR ───────────────────────────────────────────────────
            int bestValue = 0;

            foreach (var att in message.Attachments)
            {
                if (!att.ContentType?.StartsWith("image") ?? true)
                    continue;

                string tmp = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.png");

                using var http = new HttpClient();
                var data = await http.GetByteArrayAsync(att.Url);
                await File.WriteAllBytesAsync(tmp, data);

                int value = 0;

                if (mode == "gold")
                {
                    value = await _ocrService.ExtractDeliveryDonationAmountAsync(tmp) ?? 0;
                }
                else
                {
                    var text = await _ocrService.ExtractRawTextAsync(tmp);
                    value = int.TryParse(text, out var v) ? v : 0;
                }

                File.Delete(tmp);

                if (value > bestValue)
                    bestValue = value;
            }

            if (bestValue <= 0)
            {
                await message.AddReactionAsync(new Emoji("❌"));
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error("Could not detect a valid value from the screenshot."));
                return;
            }

            // ── Threshold checks + registration ──────────────────────
            if (mode == "gold")
            {
                if (bestValue < 5_000_000)
                {
                    await message.AddReactionAsync(new Emoji("❌"));
                    await message.Channel.SendMessageAsync(embed:
                        EmbedHelper.Error($"Requirement not met (≥ {GoldRequirement} gold)."));
                    return;
                }

                await _deliveryService.RegisterGoldAsync(member, bestValue);

                await message.AddReactionAsync(new Emoji("💰"));
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Success(
                        uploaderDiscordId != member.DiscordUserId
                            ? $"💰 Gold contribution recorded for **{member.IngameName}**."
                            : "💰 Gold contribution recorded."
                    ));
            }
            else
            {
                if (bestValue < BraceletRequirement)
                {
                    await message.AddReactionAsync(new Emoji("❌"));
                    await message.Channel.SendMessageAsync(embed:
                        EmbedHelper.Error($"Requirement not met (≥ {BraceletRequirement} bracelets)."));
                    return;
                }

                await _deliveryService.RegisterBraceletAsync(member, bestValue);

                await message.AddReactionAsync(new Emoji("🟢"));
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Success(
                        uploaderDiscordId != member.DiscordUserId
                            ? $"📿 Bracelet contribution recorded for **{member.IngameName}**."
                            : "📿 Bracelet contribution recorded."
                    ));
            }

            _submissionMode.Remove(message.Author.Id);
        }

        // ============================================================
        // PERMISSION
        // ============================================================
        private bool IsOfficer(SocketMessage message)
        {
            if (message.Channel is not SocketGuildChannel gc)
            {
                _ = message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error("This command must be used inside the server."));
                return false;
            }

            var user = gc.GetUser(message.Author.Id);

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