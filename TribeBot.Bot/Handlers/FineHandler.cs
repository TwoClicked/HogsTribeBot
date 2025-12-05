using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using TribeBot.Core.Entities;
using TribeBot.Core.Interfaces;
using TribeBot.Services.Services;

namespace TribeBot.Bot.Handlers
{
    public class FineHandler
    {
        private readonly DiscordSocketClient _client;
        private readonly IFineService _fineService;
        private readonly IMemberService _memberService;
        private readonly PaddleOcrServerService _ocrService;

        private const ulong OfficerRoleId = 1222665812775534592;
        private const ulong FinePaymentChannelId = 1440431172160061450;
        private const ulong OfficerLogChannelId = 1440209811621937273;

        public FineHandler(
            DiscordSocketClient client,
            IFineService fineService,
            IMemberService memberService,
            PaddleOcrServerService ocrService)
        {
            _client = client;
            _fineService = fineService;
            _memberService = memberService;
            _ocrService = ocrService;
        }

        // ======================================================================
        // ROOT ENTRY
        // ======================================================================
        public async Task<bool> TryHandleAsync(SocketMessage message)
        {
            if (message.Author.IsBot)
                return false;

            string content = message.Content.Trim();

            // Officer-only commands
            if (content.StartsWith("!fineuser", StringComparison.OrdinalIgnoreCase))
            {
                await IssueEventFine(message);
                return true;
            }

            if (content.StartsWith("!finereign", StringComparison.OrdinalIgnoreCase))
            {
                await IssueReignFine(message);
                return true;
            }

            if (content.Equals("!finelist", StringComparison.OrdinalIgnoreCase))
            {
                await ShowFineList(message);
                return true;
            }

            if (content.StartsWith("!removefine", StringComparison.OrdinalIgnoreCase))
            {
                await RemoveFine(message);
                return true;
            }

            // User: show personal fines
            if (content.Equals("!myfines", StringComparison.OrdinalIgnoreCase))
            {
                await ShowMyFines(message);
                return true;
            }

            // OCR fine payment
            if (message.Channel.Id == FinePaymentChannelId && message.Attachments.Any())
            {
                await ProcessFinePayment(message);
                return true;
            }

            return false;
        }

        // ======================================================================
        // OFFICER: !FINEUSER @user amount reason
        // ======================================================================
        private async Task IssueEventFine(SocketMessage message)
        {
            if (!IsOfficer(message)) return;

            if (message.MentionedUsers.Count == 0)
            {
                await message.Channel.SendMessageAsync("Usage: `!fineuser @user amount reason`");
                return;
            }

            var targetUser = message.MentionedUsers.First();

            var parts = message.Content.Split(' ', 4);
            if (parts.Length < 4)
            {
                await message.Channel.SendMessageAsync("Usage: `!fineuser @user amount reason`");
                return;
            }

            if (!int.TryParse(parts[2], out int amount) || amount <= 0)
            {
                await message.Channel.SendMessageAsync("❌ Invalid amount.");
                return;
            }

            string reason = parts[3];

            var member = await _memberService.GetMemberByDiscordIdAsync(targetUser.Id.ToString());
            if (member == null)
            {
                await message.Channel.SendMessageAsync($"❌ <@{targetUser.Id}> is not registered.");
                return;
            }

            await _fineService.AddEventFineAsync(member, amount, reason);

            await message.Channel.SendMessageAsync(
                $"💸 **Fine Issued**\n" +
                $"• User: <@{targetUser.Id}>\n" +
                $"• Amount: **{amount:N0}**\n" +
                $"• Reason: *{reason}*\n" +
                $"• Type: **Event Fine**"
            );

            // DM user
            try
            {
                var dm = await targetUser.CreateDMChannelAsync();
                await dm.SendMessageAsync(
                    $"⚠️ **You received an Event Fine!**\n" +
                    $"Amount: **{amount:N0}**\n" +
                    $"Reason: {reason}\n" +
                    $"Pay it in <#{FinePaymentChannelId}>.");
            }
            catch { }
        }

        // ======================================================================
        // OFFICER: !FINEREIGN @user amount reason
        // ======================================================================
        private async Task IssueReignFine(SocketMessage message)
        {
            if (!IsOfficer(message)) return;

            if (message.MentionedUsers.Count == 0)
            {
                await message.Channel.SendMessageAsync("Usage: `!finereign @user amount reason`");
                return;
            }

            var targetUser = message.MentionedUsers.First();
            var parts = message.Content.Split(' ', 4);

            if (parts.Length < 4)
            {
                await message.Channel.SendMessageAsync("Usage: `!finereign @user amount reason`");
                return;
            }

            if (!int.TryParse(parts[2], out int amount) || amount <= 0)
            {
                await message.Channel.SendMessageAsync("❌ Invalid amount.");
                return;
            }

            string reason = parts[3];

            var member = await _memberService.GetMemberByDiscordIdAsync(targetUser.Id.ToString());
            if (member == null)
            {
                await message.Channel.SendMessageAsync($"❌ <@{targetUser.Id}> is not registered.");
                return;
            }

            // Count ALL previous reign fines (even paid ones)
            var allFines = await _fineService.GetAllFinesAsync();
            int previousReignFines = allFines
                .Where(f => f.DiscordUserId == member.DiscordUserId &&
                            f.FineType == "Reign")
                .Count();

            bool repeatOffense = previousReignFines >= 1;

            await _fineService.AddReignFineAsync(member, amount, reason);

            string text = repeatOffense
                ? "Strikes added: **2** (repeat offense)"
                : "No strikes added (first offense)";

            await message.Channel.SendMessageAsync(
                $"⚔️ **Reign Fine Issued**\n" +
                $"• User: <@{targetUser.Id}>\n" +
                $"• Amount: **{amount:N0}**\n" +
                $"• Reason: *{reason}*\n" +
                $"{text}"
            );

            // DM the user
            try
            {
                var dm = await targetUser.CreateDMChannelAsync();

                if (repeatOffense)
                {
                    await dm.SendMessageAsync(
                        $"⚠️ **Repeat Reign Fine!**\n" +
                        $"Amount: **{amount:N0}**\nReason: {reason}\n" +
                        $"Strikes Added: **2**\n" +
                        $"🚫 You are blacklisted from the next two Reign events.\n" +
                        $"Pay in <#{FinePaymentChannelId}>.");
                }
                else
                {
                    await dm.SendMessageAsync(
                        $"⚠️ **You received a Reign Fine.**\n" +
                        $"Amount: **{amount:N0}**\nReason: {reason}\n" +
                        $"No strikes added for first offense.\n" +
                        $"Pay in <#{FinePaymentChannelId}>.");
                }
            }
            catch { }
        }

        // ======================================================================
        // OCR FINE PAYMENT
        // ======================================================================
        private async Task ProcessFinePayment(SocketMessage message)
        {
            string discordId = message.Author.Id.ToString();

            var targetMember = await _memberService.GetMemberByDiscordIdAsync(discordId);
            if (targetMember == null)
            {
                await message.Channel.SendMessageAsync($"{message.Author.Mention} ❌ You are not registered.");
                return;
            }

            int total = 0;

            foreach (var att in message.Attachments)
            {
                if (att.ContentType == null || !att.ContentType.StartsWith("image"))
                    continue;

                string tmp = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.png");

                using (var client = new HttpClient())
                {
                    var data = await client.GetByteArrayAsync(att.Url);
                    await File.WriteAllBytesAsync(tmp, data);
                }

                int? amount = await _ocrService.ExtractDonationAmountAsync(tmp);
                File.Delete(tmp);

                if (amount.HasValue)
                    total += amount.Value;
            }

            if (total <= 0)
            {
                await message.Channel.SendMessageAsync(
                    $"{message.Author.Mention} ❌ I couldn’t read a valid fine payment amount.");
                return;
            }

            await _fineService.AddPaymentAsync(targetMember.DiscordUserId, total);

            await message.AddReactionAsync(new Emoji("💸"));
            await message.Channel.SendMessageAsync(
                $"{message.Author.Mention} **Payment received!**\n" +
                $"An officer will verify shortly.\n\n" +
                $"⚠️ Reign fines remain visible until strike resets.");
        }

        // ======================================================================
        // USER: !MYFINES
        // ======================================================================
        private async Task ShowMyFines(SocketMessage message)
        {
            string id = message.Author.Id.ToString();
            var fines = await _fineService.GetFinesForUserAsync(id);

            if (fines.Count == 0)
            {
                await message.Channel.SendMessageAsync("🎉 You have no fines!");
                return;
            }

            string unpaid = "";
            string paid = "";
            int totalOwed = 0;

            foreach (var f in fines)
            {
                if (!f.IsPaid)
                {
                    unpaid += $"• **{f.Amount:N0}** — {f.FineType} — FineID `{f.FineId}` " +
                              $"({f.PaidAmount:N0}/{f.Amount:N0} paid)\n";
                    totalOwed += (f.Amount - f.PaidAmount);
                }
                else
                {
                    paid += $"• **{f.Amount:N0}** — {f.FineType} — FineID `{f.FineId}` — PAID\n";
                }
            }

            string msg =
                $"🧾 **Your Fines**\n\n" +
                "🟥 **Unpaid:**\n" +
                (string.IsNullOrEmpty(unpaid) ? "• None\n" : unpaid) +
                $"\n**Total Owed: {totalOwed:N0}**\n\n" +
                "🟩 **Paid / Awaiting Removal:**\n" +
                (string.IsNullOrEmpty(paid) ? "• None\n" : paid);

            await message.Channel.SendMessageAsync(msg);
        }

        // ======================================================================
        // OFFICER: !FINELIST
        // ======================================================================
        private async Task ShowFineList(SocketMessage message)
        {
            if (!IsOfficer(message)) return;

            var unpaid = await _fineService.GetUnpaidFinesAsync();
            var paid = await _fineService.GetPaidFinesAsync();

            string msg = "💀 **Fine List**\n\n";

            msg += "🟥 **UNPAID FINES**\n";
            if (unpaid.Count == 0)
            {
                msg += "• None\n";
            }
            else
            {
                foreach (var f in unpaid)
                {
                    msg +=
                        $"• **{f.IngameName}** — {f.Amount:N0} — `{f.FineType}` — FineID `{f.FineId}` — " +
                        $"({f.PaidAmount:N0}/{f.Amount:N0})\n";
                }
            }

            msg += "\n🟩 **PAID (Awaiting Removal)**\n";
            if (paid.Count == 0)
            {
                msg += "• None\n";
            }
            else
            {
                foreach (var f in paid)
                {
                    msg +=
                        $"• **{f.IngameName}** — {f.Amount:N0} — `{f.FineType}` — FineID `{f.FineId}` — PAID\n";
                }
            }

            await message.Channel.SendMessageAsync(msg);
        }

        // ======================================================================
        // OFFICER: !REMOVEFINE FineId
        // ======================================================================
        private async Task RemoveFine(SocketMessage message)
        {
            if (!IsOfficer(message)) return;

            var parts = message.Content.Split(' ', 2);
            if (parts.Length < 2)
            {
                await message.Channel.SendMessageAsync("Usage: `!removefine FineId`");
                return;
            }

            string fineId = parts[1].Trim();

            await _fineService.RemoveFineAsync(fineId);

            await message.Channel.SendMessageAsync($"🗑️ Removed fine `{fineId}`.");
        }

        // ======================================================================
        // HELPER — OFFICER CHECK
        // ======================================================================
        private bool IsOfficer(SocketMessage message)
        {
            if (message.Channel is not SocketGuildChannel gc)
            {
                message.Channel.SendMessageAsync("❌ Must be used inside the server.");
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
    }
}
