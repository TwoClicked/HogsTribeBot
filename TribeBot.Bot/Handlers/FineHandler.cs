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
        private const ulong OfficerLogChannelId = 1440211043820507217;

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
        // EMBED HELPERS (LOCAL)
        // ======================================================================
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

            foreach (var kv in fields)
                eb.AddField(kv.Key, kv.Value, true);

            return OfficerLog?.SendMessageAsync(embed: eb.Build()) ?? Task.CompletedTask;
        }

        // ======================================================================
        // ROOT ENTRY
        // ======================================================================
        public async Task<bool> TryHandleAsync(SocketMessage message)
        {
            if (message.Author.IsBot) return false;

            string content = message.Content.Trim().ToLower();

            if (content.StartsWith("!fineuser"))
            {
                await IssueEventFine(message);
                return true;
            }

            if (content.StartsWith("!finereign"))
            {
                await IssueReignFine(message);
                return true;
            }

            if (content.Equals("!finelist"))
            {
                await ShowFineList(message);
                return true;
            }

            if (content.StartsWith("!removefine"))
            {
                await RemoveFine(message);
                return true;
            }

            if (content.Equals("!myfines"))
            {
                await ShowMyFines(message);
                return true;
            }

            // OCR payment
            if (message.Channel.Id == FinePaymentChannelId && message.Attachments.Any())
            {
                await ProcessFinePayment(message);
                return true;
            }

            // Verify payment
            if (content.StartsWith("!verifiedpayment"))
            {
                await VerifyPayment(message);
                return true;
            }

            return false;
        }

        // ======================================================================
        // !VERIFIEDPAYMENT
        // ======================================================================
        private async Task VerifyPayment(SocketMessage message)
        {
            if (!IsOfficer(message)) return;

            if (message.MentionedUsers.Count == 0)
            {
                await SendWarning(message, "Usage: `!verifiedpayment @user`");
                return;
            }

            var user = message.MentionedUsers.First();
            var member = await _memberService.GetMemberByDiscordIdAsync(user.Id.ToString());

            if (member == null)
            {
                await SendError(message, $"{user.Username} is not registered.");
                return;
            }

            var fines = await _fineService.GetFinesForUserAsync(member.DiscordUserId);
            var unpaid = fines.Where(f => !f.IsPaid).ToList();

            if (!unpaid.Any())
            {
                await SendInfo(message, "Fine Status", $"{user.Mention} has no unpaid fines.");
                return;
            }

            foreach (var fine in unpaid)
            {
                fine.PaidAmount = fine.Amount;
                fine.IsPaid = true;
                await _fineService.UpdateFineAsync(fine);
            }

            await SendSuccess(message, $"Verified manual payment for {user.Mention}. All fines marked as **PAID**.");

            // DM user
            try
            {
                var dm = await user.CreateDMChannelAsync();
                await dm.SendMessageAsync(
                    $"✅ **Your payment was manually verified by an officer.**\n" +
                    $"All your fines are now marked **PAID**.\n" +
                    $"Reign strikes still resolve only at the next scheduled reset.");
            }
            catch { }

            await SendLog("Manual Fine Payment Verified", new()
            {
                { "User", user.Username },
                { "Officer", message.Author.Username }
            });
        }

        // ======================================================================
        // !FINEUSER
        // ======================================================================
        private async Task IssueEventFine(SocketMessage message)
        {
            if (!IsOfficer(message)) return;

            if (message.MentionedUsers.Count == 0)
            {
                await SendWarning(message, "Usage: `!fineuser @user amount reason`");
                return;
            }

            var target = message.MentionedUsers.First();
            var parts = message.Content.Split(' ', 4);
            if (parts.Length < 4)
            {
                await SendWarning(message, "Usage: `!fineuser @user amount reason`");
                return;
            }

            if (!int.TryParse(parts[2], out int amount) || amount <= 0)
            {
                await SendError(message, "Invalid fine amount.");
                return;
            }

            string reason = parts[3];

            var member = await _memberService.GetMemberByDiscordIdAsync(target.Id.ToString());
            if (member == null)
            {
                await SendError(message, $"{target.Username} is not registered.");
                return;
            }

            await _fineService.AddEventFineAsync(member, amount, reason);

            await SendSuccess(
                message,
                $"💸 **Event Fine Issued**\n\n" +
                $"• User: {target.Mention}\n" +
                $"• Amount: **{amount:N0}**\n" +
                $"• Reason: *{reason}*\n" +
                $"• Type: **Event Fine**"
            );

            // DM user
            try
            {
                var dm = await target.CreateDMChannelAsync();
                await dm.SendMessageAsync(
                    $"⚠️ **You received an Event Fine!**\n" +
                    $"Amount: **{amount:N0}**\nReason: {reason}\n" +
                    $"Please pay it in <#{FinePaymentChannelId}>.");
            }
            catch { }

            await SendLog("Event Fine Issued", new()
            {
                { "User", target.Username },
                { "Officer", message.Author.Username },
                { "Amount", amount.ToString("N0") },
                { "Reason", reason }
            });
        }

        // ======================================================================
        // !FINEREIGN
        // ======================================================================
        private async Task IssueReignFine(SocketMessage message)
        {
            if (!IsOfficer(message)) return;

            if (message.MentionedUsers.Count == 0)
            {
                await SendWarning(message, "Usage: `!finereign @user amount reason`");
                return;
            }

            var target = message.MentionedUsers.First();
            var parts = message.Content.Split(' ', 4);
            if (parts.Length < 4)
            {
                await SendWarning(message, "Usage: `!finereign @user amount reason`");
                return;
            }

            if (!int.TryParse(parts[2], out int amount) || amount <= 0)
            {
                await SendError(message, "Invalid fine amount.");
                return;
            }

            string reason = parts[3];

            var member = await _memberService.GetMemberByDiscordIdAsync(target.Id.ToString());
            if (member == null)
            {
                await SendError(message, $"{target.Username} is not registered.");
                return;
            }

            var allFines = await _fineService.GetAllFinesAsync();
            bool repeatOffense = allFines.Any(f =>
                f.DiscordUserId == member.DiscordUserId &&
                f.FineType == "Reign");

            await _fineService.AddReignFineAsync(member, amount, reason);

            string extra = repeatOffense
                ? "Strikes added: **2** (repeat offense)"
                : "No strikes added (first offense)";

            await SendSuccess(
                message,
                $"⚔️ **Reign Fine Issued**\n\n" +
                $"• User: {target.Mention}\n" +
                $"• Amount: **{amount:N0}**\n" +
                $"• Reason: *{reason}*\n" +
                $"{extra}"
            );

            // DM user
            try
            {
                var dm = await target.CreateDMChannelAsync();

                if (repeatOffense)
                {
                    await dm.SendMessageAsync(
                        $"⚠️ **Repeat Reign Fine!**\n" +
                        $"Amount: **{amount:N0}**\nReason: {reason}\n" +
                        $"Strikes Added: **2**\n" +
                        $"🚫 You are blacklisted from the next **two** Reign events.\n" +
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

            await SendLog("Reign Fine Issued", new()
            {
                { "User", target.Username },
                { "Amount", amount.ToString("N0") },
                { "Officer", message.Author.Username },
                { "Reason", reason },
                { "Repeat Offense", repeatOffense ? "Yes" : "No" }
            });
        }

        // ======================================================================
        // OCR FINE PAYMENT
        // ======================================================================
        private async Task ProcessFinePayment(SocketMessage message)
        {
            string discordId = message.Author.Id.ToString();
            var member = await _memberService.GetMemberByDiscordIdAsync(discordId);

            if (member == null)
            {
                await SendError(message, "You are not registered.");
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

                int? amt = await _ocrService.ExtractDonationAmountAsync(tmp);
                File.Delete(tmp);

                if (amt.HasValue)
                    total += amt.Value;
            }

            if (total <= 0)
            {
                await SendError(message, "I could not read any fine payment amount.");
                return;
            }

            await _fineService.AddPaymentAsync(discordId, total);

            await message.AddReactionAsync(new Emoji("💸"));
            await SendSuccess(
                message,
                $"Payment received! An officer will verify shortly.\n\n⚠️ Reign fines remain visible until strike resets."
            );
        }

        // ======================================================================
        // !MYFINES
        // ======================================================================
        private async Task ShowMyFines(SocketMessage message)
        {
            string id = message.Author.Id.ToString();
            var fines = await _fineService.GetFinesForUserAsync(id);

            if (fines.Count == 0)
            {
                await SendSuccess(message, "🎉 You have no fines!");
                return;
            }

            string unpaid = "";
            string paid = "";
            int totalOwed = 0;

            foreach (var f in fines)
            {
                if (!f.IsPaid)
                {
                    unpaid +=
                        $"• **{f.Amount:N0}** — {f.FineType} — FineID `{f.FineId}` " +
                        $"({f.PaidAmount:N0}/{f.Amount:N0} paid)\n";

                    totalOwed += (f.Amount - f.PaidAmount);
                }
                else
                {
                    paid +=
                        $"• **{f.Amount:N0}** — {f.FineType} — FineID `{f.FineId}` — PAID\n";
                }
            }

            string msg =
                $"🧾 **Your Fines**\n\n" +
                "🟥 **Unpaid:**\n" +
                (string.IsNullOrEmpty(unpaid) ? "• None\n" : unpaid) +
                $"\n**Total Owed: {totalOwed:N0}**\n\n" +
                "🟩 **Paid / Awaiting Removal:**\n" +
                (string.IsNullOrEmpty(paid) ? "• None\n" : paid);

            await SendInfo(message, "Your Fines", msg);
        }

        // ======================================================================
        // !FINELIST
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

            await SendInfo(message, "Fine List", msg);
        }

        // ======================================================================
        // !REMOVEFINE
        // ======================================================================
        private async Task RemoveFine(SocketMessage message)
        {
            if (!IsOfficer(message)) return;

            var parts = message.Content.Split(' ', 2);
            if (parts.Length < 2)
            {
                await SendWarning(message, "Usage: `!removefine FineId`");
                return;
            }

            string fineId = parts[1].Trim();
            await _fineService.RemoveFineAsync(fineId);

            await SendSuccess(message, $"🗑️ Removed fine `{fineId}`.");

            await SendLog("Fine Removed", new()
            {
                { "Fine ID", fineId },
                { "Officer", message.Author.Username }
            });
        }

        // ======================================================================
        // OFFICER CHECK
        // ======================================================================
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
