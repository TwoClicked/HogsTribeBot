using Discord;
using Discord.WebSocket;
using System.Linq;
using System.Threading.Tasks;
using TribeBot.Core.Interfaces;

namespace TribeBot.Bot.Handlers
{
    public class GeneralHandler
    {
        private readonly DiscordSocketClient _client;
        private readonly IMemberService _memberService;
        private readonly IDonationService _donationService;
        private readonly IFineService _fineService;

        private const ulong OfficerRoleId = 1222665812775534592;

        public GeneralHandler(
            DiscordSocketClient client,
            IMemberService memberService,
            IDonationService donationService,
            IFineService fineService)
        {
            _client = client;
            _memberService = memberService;
            _donationService = donationService;
            _fineService = fineService;
        }

        public async Task<bool> TryHandleAsync(SocketMessage message)
        {
            if (message.Author.IsBot)
                return false;

            string content = message.Content.Trim();

            if (content.Equals("!myinfo", System.StringComparison.OrdinalIgnoreCase))
            {
                await MyInfo(message);
                return true;
            }

            if (content.Equals("!listmembers", System.StringComparison.OrdinalIgnoreCase))
            {
                await ListMembers(message);
                return true;
            }

            if (content.StartsWith("!viewinfo", System.StringComparison.OrdinalIgnoreCase))
            {
                await ViewInfo(message);
                return true;
            }

            return false;
        }

        // ============================================================
        // !myinfo
        // ============================================================
        private async Task MyInfo(SocketMessage message)
        {
            string id = message.Author.Id.ToString();
            var member = await _memberService.GetMemberByDiscordIdAsync(id);

            if (member == null)
            {
                await message.Channel.SendMessageAsync(
                    $"{message.Author.Mention} you are not registered. Use `!register`.");
                return;
            }

            var donations = await _donationService.GetTotalForUserThisWeekAsync(id);

            string donationStatus =
                member.IsExempt ? "🟦 EXEMPT" :
                donations > 0 ? "✅ PAID" : "❌ UNPAID";

            string reply =
                $"🧾 **Your Profile**\n\n" +
                $"**Name:** {member.IngameName}\n" +
                $"**ID:** {member.IngameId}\n" +
                $"**Might:** {member.Might:N0}\n" +
                $"**Kills:** {member.KillPoints:N0}\n" +
                $"**Collector:** {member.CollectorLevel}\n" +
                $"**Reign Points:** {member.ReignPoints}\n" +
                $"**Exempt:** {(member.IsExempt ? "Yes" : "No")}\n" +
                $"**Donation:** {donationStatus}\n" +
                $"**Updated:** {member.LastUpdatedUTC:yyyy-MM-dd HH:mm} UTC";

            await message.Channel.SendMessageAsync(reply);
        }

        // ============================================================
        // !listmembers
        // ============================================================
        private async Task ListMembers(SocketMessage message)
        {
            var members = await _memberService.GetAllMembersAsync();

            if (members.Count == 0)
            {
                await message.Channel.SendMessageAsync("No members found.");
                return;
            }

            string msg = "📜 **Member List (A–Z)**\n\n";

            foreach (var m in members.OrderBy(m => m.IngameName))
                msg += $"• **{m.IngameName}** — `{m.IngameId}`\n";

            await SendLong(message.Channel, msg);
        }

        // ============================================================
        // !viewinfo @user (Officer-only)
        // ============================================================
        private async Task ViewInfo(SocketMessage message)
        {
            if (!IsOfficer(message))
                return;

            if (message.MentionedUsers.Count == 0)
            {
                await message.Channel.SendMessageAsync("Usage: `!viewinfo @user`");
                return;
            }

            var target = message.MentionedUsers.First();
            string id = target.Id.ToString();

            var member = await _memberService.GetMemberByDiscordIdAsync(id);
            if (member == null)
            {
                await message.Channel.SendMessageAsync($"❌ <@{target.Id}> is not registered.");
                return;
            }

            var donations = await _donationService.GetTotalForUserThisWeekAsync(id);

            string donationStatus =
                member.IsExempt ? "🟦 EXEMPT" :
                donations > 0 ? "✅ PAID" : "❌ UNPAID";

            var fines = await _fineService.GetFinesForUserAsync(id);
            var unpaid = fines.Where(f => !f.IsPaid).ToList();
            var paid = fines.Where(f => f.IsPaid).ToList();

            string unpaidText = unpaid.Count == 0
                ? "• None\n"
                : string.Join("", unpaid.Select(f =>
                    $"• {f.Amount:N0} — {f.FineType} — FineID `{f.FineId}` ({f.PaidAmount:N0}/{f.Amount:N0})\n"));

            string paidText = paid.Count == 0
                ? "• None\n"
                : string.Join("", paid.Select(f =>
                    $"• {f.Amount:N0} — {f.FineType} — FineID `{f.FineId}` — PAID\n"));

            string reply =
                $"📘 **Profile for <@{target.Id}>**\n\n" +
                $"**Name:** {member.IngameName}\n" +
                $"**ID:** {member.IngameId}\n" +
                $"**Might:** {member.Might:N0}\n" +
                $"**Kills:** {member.KillPoints:N0}\n" +
                $"**Collector:** {member.CollectorLevel}\n" +
                $"**Reign Points:** {member.ReignPoints}\n" +
                $"**Exempt:** {member.IsExempt}\n\n" +
                $"🏦 **Donation:** {donationStatus}\n\n" +
                $"💀 **Unpaid Fines:**\n{unpaidText}\n" +
                $"🟩 **Paid Fines:**\n{paidText}\n" +
                $"🕒 **Last Updated:** {member.LastUpdatedUTC:yyyy-MM-dd HH:mm} UTC";

            await SendLong(message.Channel, reply);
        }

        // ============================================================
        // HELPERS
        // ============================================================
        private bool IsOfficer(SocketMessage message)
        {
            if (message.Channel is not SocketGuildChannel gc)
            {
                message.Channel.SendMessageAsync("❌ Must be used in guild.");
                return false;
            }

            var user = gc.GetUser(message.Author.Id);
            if (user == null || !user.Roles.Any(r => r.Id == OfficerRoleId))
            {
                message.Channel.SendMessageAsync($"{message.Author.Mention} ❌ No permission.");
                return false;
            }

            return true;
        }

        private async Task SendLong(IMessageChannel ch, string text)
        {
            const int limit = 1990;
            if (text.Length <= limit)
            {
                await ch.SendMessageAsync(text);
                return;
            }

            var lines = text.Split('\n');
            string buf = "";
            foreach (var line in lines)
            {
                if ((buf + line).Length > limit)
                {
                    await ch.SendMessageAsync(buf);
                    buf = "";
                }
                buf += line + "\n";
            }

            if (buf.Length > 0)
                await ch.SendMessageAsync(buf);
        }
    }
}
