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

        private readonly Dictionary<ulong, string> _submissionMode = new();

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
                        $"💰 Upload your screenshot now.\n\nRequirement: **≥ 75,000,000 gold**"
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
                        $"📿 Upload your screenshot now.\n\nRequirement: **≥ 1000 bracelets**"
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

            EmbedHelper.Success(
                $"🚀 **Delivery Event Started**\n\n" +
                $"For Delivery you have two options:\n" +
                $"📿 ≥ 1000 bracelets\n" +
                $"💰 ≥ 75,000,000 gold\n\n" +
                $"Submit in <#{DeliveryChannelId}>"
            );

            await OfficerLog?.SendMessageAsync(embed:
                EmbedHelper.Log("Delivery Event Started", new()
                {
                    { "EventId", eventId },
                    { "Started By", message.Author.Username }
                }));
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
                                $"Fine Amount: **150,000,000 gold**\n\n" +
                                $"Please pay in <#{FinePaymentChannelId}>.\n" +
                                $"If you believe this is incorrect, contact an officer."
                            ); ;

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

            // =========================
            // OFFICER LOG (BANK STYLE)
            // =========================

            var fields = new Dictionary<string, string>
             {
                 { "Mode", "Manual Event End" },
                 { "EventId", eventId },
                 { "Fines Issued", finedMembers.Count.ToString() },
                 { "Amount", "150,000,000" },
                 { "DM Sent", dmSent.ToString() }
             };

            if (finedList.Any())
                fields["Fined Members"] = string.Join("\n", finedList);

            if (dmFailures.Any())
                fields["DM Failures"] = string.Join("\n", dmFailures);

            await OfficerLog?.SendMessageAsync(embed:
                EmbedHelper.Log("Delivery Event Fines Issued", fields));

            // =========================
            // USER-FACING MESSAGE
            // =========================

            await message.Channel.SendMessageAsync(embed:
                EmbedHelper.Warning(
                    $"❌ **Delivery Event Ended**\n\n" +
                    $"Fines Issued: **{finedMembers.Count}**\n" +
                    $"DM Sent: **{dmSent}**\n" +
                    $"Amount: **150,000,000**"
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

            var member = await _memberService.GetMemberByDiscordIdAsync(message.Author.Id.ToString());

            if (member == null)
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error("You are not registered."));
                return;
            }

            if (!_submissionMode.TryGetValue(message.Author.Id, out string mode))
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Warning("Use **gold** or **bracelet** first."));
                return;
            }

            int bestValue = 0;

            foreach (var att in message.Attachments)
            {
                if (!att.ContentType?.StartsWith("image") ?? true)
                    continue;

                string tmp = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.png");

                using var client = new HttpClient();
                var data = await client.GetByteArrayAsync(att.Url);
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
                    EmbedHelper.Error("Could not detect valid value."));
                return;
            }

            if (mode == "gold")
            {
                if (bestValue < 75_000_000)
                {
                    await message.AddReactionAsync(new Emoji("❌"));
                    await message.Channel.SendMessageAsync(embed:
                        EmbedHelper.Error("Requirement not met (≥ 75M gold)."));
                    return;
                }

                await _deliveryService.RegisterGoldAsync(member, bestValue);

                await message.AddReactionAsync(new Emoji("💰"));
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Success("💰 Gold contribution recorded."));
            }
            else
            {
                if (bestValue < 1000)
                {
                    await message.AddReactionAsync(new Emoji("❌"));
                    await message.Channel.SendMessageAsync(embed:
                        EmbedHelper.Error("Requirement not met (≥ 1000 bracelets)."));
                    return;
                }

                await _deliveryService.RegisterBraceletAsync(member, bestValue);

                await message.AddReactionAsync(new Emoji("🟢"));
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Success("📿 Bracelet contribution recorded."));
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