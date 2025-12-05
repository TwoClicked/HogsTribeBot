using Discord;
using Discord.WebSocket;
using System.Linq;
using System.Threading.Tasks;
using TribeBot.Core.Interfaces;
using TribeBot.Data.Interfaces;

namespace TribeBot.Bot.Handlers
{
    public class ReignHandler
    {
        private readonly DiscordSocketClient _client;
        private readonly IReignService _reignService;
        private readonly IFineService _fineService;
        private readonly IMemberService _memberService;
        private readonly IGoogleSheetsDataStore _dataStore;

        private const ulong GuildId = 1109193500664287336;
        private const ulong ReignOfficerRoleId = 1364209274322157639;
        private const ulong VrSubmissionChannelId = 1429640756104265829;
        private const ulong OfficerLogChannelId = 1440209811621937273;

        public ReignHandler(
            DiscordSocketClient client,
            IReignService reignService,
            IFineService fineService,
            IMemberService memberService,
            IGoogleSheetsDataStore dataStore)
        {
            _client = client;
            _reignService = reignService;
            _fineService = fineService;
            _memberService = memberService;
            _dataStore = dataStore;
        }

        public async Task<bool> TryHandleAsync(SocketMessage message)
        {
            if (message.Author.IsBot) return false;

            string content = message.Content.Trim();

            if (content.Equals("!applyreign", System.StringComparison.OrdinalIgnoreCase))
            {
                await ApplyReign(message);
                return true;
            }

            if (content.Equals("!listreign", System.StringComparison.OrdinalIgnoreCase))
            {
                await ListReign(message);
                return true;
            }

            if (content.Equals("!clearreign", System.StringComparison.OrdinalIgnoreCase))
            {
                await ClearReign(message);
                return true;
            }

            if (content.Equals("!lockreign", System.StringComparison.OrdinalIgnoreCase))
            {
                await LockReign(message);
                return true;
            }

            if (content.Equals("!unlockreign", System.StringComparison.OrdinalIgnoreCase))
            {
                await UnlockReign(message);
                return true;
            }

            if (content.StartsWith("!exempt", System.StringComparison.OrdinalIgnoreCase))
            {
                await SetExempt(message, true);
                return true;
            }

            if (content.StartsWith("!unexempt", System.StringComparison.OrdinalIgnoreCase))
            {
                await SetExempt(message, false);
                return true;
            }

            return false;
        }

        // ============================================================
        // APPLY
        // ============================================================

        private async Task ApplyReign(SocketMessage message)
        {
            var locked = await _reignService.GetReignLockedAsync();
            if (locked)
            {
                await message.Channel.SendMessageAsync(
                    "⛔ Reign is locked. Contact an officer.");
                return;
            }

            if (message.Channel.Id != VrSubmissionChannelId)
            {
                await message.Channel.SendMessageAsync(
                    "❌ You can only apply in <#1429640756104265829>.");
                return;
            }

            // Check reign strikes
            var fines = await _fineService.GetFinesForUserAsync(message.Author.Id.ToString());
            int activeStrikes = fines
                .Where(f => f.FineType == "Reign" && !f.IsPaid)
                .Sum(f => f.ReignStrikes);

            if (activeStrikes > 0)
            {
                await message.Channel.SendMessageAsync(
                    $"{message.Author.Mention} ❌ You have **{activeStrikes} Reign Strike(s)**.");
                return;
            }

            var member = await _memberService.GetMemberByDiscordIdAsync(message.Author.Id.ToString());
            if (member == null)
            {
                await message.Channel.SendMessageAsync(
                    $"{message.Author.Mention} ❌ Please register first using `!register`.");
                return;
            }

            await _reignService.ApplyAsync(message.Author.Id.ToString());

            await message.Channel.SendMessageAsync(
                $"{message.Author.Mention} ✅ Added to Viking Reign! Use `!listreign` to view ranking.");
        }

        // ============================================================
        // LIST
        // ============================================================

        private async Task ListReign(SocketMessage message)
        {
            var results = await _reignService.GetCurrentRegistrationsSortedAsync();

            if (results.Count == 0)
            {
                await message.Channel.SendMessageAsync("Nobody has applied yet.");
                return;
            }

            int pos = 1;
            string msg = "🏆 **Viking Reign Applicants**\n\n";

            foreach (var (member, reg) in results)
            {
                msg += $"{pos}) **{member.IngameName}** — {member.ReignPoints} pts\n";
                pos++;
            }

            await message.Channel.SendMessageAsync(msg);
        }

        // ============================================================
        // CLEAR (OFFICER ONLY)
        // ============================================================

        private async Task ClearReign(SocketMessage message)
        {
            if (!await IsOfficer(message)) return;

            await _reignService.ClearAsync();
            await message.Channel.SendMessageAsync("🧹 Reign list cleared.");
        }

        // ============================================================
        // LOCK / UNLOCK (OFFICER ONLY)
        // ============================================================

        private async Task LockReign(SocketMessage message)
        {
            if (!await IsOfficer(message)) return;

            await _reignService.SetReignLockedAsync(true);
            await _fineService.ReduceReignStrikesAsync();

            await message.Channel.SendMessageAsync("🔒 Reign is now LOCKED.");
        }

        private async Task UnlockReign(SocketMessage message)
        {
            if (!await IsOfficer(message)) return;

            await _reignService.SetReignLockedAsync(false);
            await message.Channel.SendMessageAsync("🔓 Reign is now UNLOCKED.");
        }

        // ============================================================
        // EXEMPT / UNEXEMPT (OFFICER ONLY)
        // ============================================================

        private async Task SetExempt(SocketMessage message, bool exempt)
        {
            if (!await IsOfficer(message)) return;

            if (message.MentionedUsers.Count == 0)
            {
                await message.Channel.SendMessageAsync("Usage: `!exempt @user` or `!unexempt @user`");
                return;
            }

            var target = message.MentionedUsers.First();
            var member = await _memberService.GetMemberByDiscordIdAsync(target.Id.ToString());

            if (member == null)
            {
                await message.Channel.SendMessageAsync($"❌ <@{target.Id}> is not registered.");
                return;
            }

            member.IsExempt = exempt;
            member.LastUpdatedUTC = System.DateTime.UtcNow;
            await _memberService.RegisterOrUpdateAsync(member);

            string status = exempt ? "EXEMPT" : "NOT EXEMPT";
            await message.Channel.SendMessageAsync(
                $"<@{target.Id}> is now **{status}** from weekly donations.");
        }

        // ============================================================
        // HELPERS
        // ============================================================

        private async Task<bool> IsOfficer(SocketMessage message)
        {
            // The channel MUST be a guild text channel
            if (message.Channel is not SocketGuildChannel gc)
            {
                await (message.Channel as IMessageChannel)!.SendMessageAsync("❌ You must use this in the server.");
                return false;
            }

            var user = gc.GetUser(message.Author.Id);

            if (user == null || !user.Roles.Any(r => r.Id == ReignOfficerRoleId))
            {
                await (message.Channel as IMessageChannel)!.SendMessageAsync(
                    $"{message.Author.Mention} ❌ You do not have permission.");
                return false;
            }

            return true;
        }

    }
}
