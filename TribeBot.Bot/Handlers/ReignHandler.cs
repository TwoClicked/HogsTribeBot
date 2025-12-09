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
        private const ulong OfficerLogChannelId = 1440211043820507217;

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

            // Main commands
            switch (content.ToLower())
            {
                case "!applyreign":
                    await ApplyReign(message);
                    return true;

                case "!listreign":
                    await ListReign(message);
                    return true;

                case "!clearreign":
                    await ClearReign(message);
                    return true;

                case "!lockreign":
                    await LockReign(message);
                    return true;

                case "!unlockreign":
                    await UnlockReign(message);
                    return true;

                case "!leavereign":
                    await LeaveReign(message);
                    return true;
            }

            // Commands with arguments
            if (content.StartsWith("!exempt"))
            {
                await SetExempt(message, true);
                return true;
            }

            if (content.StartsWith("!unexempt"))
            {
                await SetExempt(message, false);
                return true;
            }

            if (content.StartsWith("!removereign"))
            {
                await RemoveReignMember(message);
                return true;
            }

            return false;
        }

        // ============================================================
        // Helper: Embed Builder
        // ============================================================

        private Embed BuildEmbed(string title, string desc, Color color)
        {
            return new EmbedBuilder()
                .WithTitle(title)
                .WithDescription(desc)
                .WithColor(color)
                .WithFooter($"{System.DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC")
                .Build();
        }

        private async Task SendError(SocketMessage msg, string text)
        {
            await msg.Channel.SendMessageAsync(embed:
                BuildEmbed("❌ Error", text, Color.Red));
        }

        private async Task SendSuccess(SocketMessage msg, string text)
        {
            await msg.Channel.SendMessageAsync(embed:
                BuildEmbed("🟢 Success", text, Color.Green));
        }

        private async Task SendWarning(SocketMessage msg, string text)
        {
            await msg.Channel.SendMessageAsync(embed:
                BuildEmbed("⚠️ Warning", text, Color.Orange));
        }

        private async Task SendInfo(SocketMessage msg, string title, string text)
        {
            await msg.Channel.SendMessageAsync(embed:
                BuildEmbed($"🛡️ {title}", text, Color.Blue));
        }

        // ============================================================
        // USER LEAVES REIGN
        // ============================================================

        private async Task LeaveReign(SocketMessage message)
        {
            var member = await _memberService.GetMemberByDiscordIdAsync(message.Author.Id.ToString());

            if (member == null)
            {
                await SendError(message, "You are not registered.");
                return;
            }

            bool removed = await _reignService.RemoveMemberFromReignAsync(message.Author.Id.ToString());
            if (!removed)
            {
                await SendError(message, "You are not part of the current reign.");
                return;
            }

            await SendSuccess(message, $"👋 {message.Author.Username}, you have left the current reign.");

            // Officer log
            var log = _client.GetChannel(OfficerLogChannelId) as IMessageChannel;
            if (log != null)
            {
                var logEmbed = new EmbedBuilder()
                    .WithTitle("📘 Reign Update — Self Removal")
                    .AddField("User", message.Author.Username, true)
                    .AddField("Action", "Left the reign", true)
                    .WithColor(new Color(0, 110, 255))
                    .WithFooter($"{System.DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC")
                    .Build();

                await log.SendMessageAsync(embed: logEmbed);
            }
        }

        // ============================================================
        // OFFICER REMOVES USER
        // ============================================================

        private async Task RemoveReignMember(SocketMessage message)
        {
            if (!await IsOfficer(message)) return;

            if (message.MentionedUsers.Count == 0)
            {
                await SendError(message, "Usage: `!removereign @user`");
                return;
            }

            var target = message.MentionedUsers.First();
            bool removed = await _reignService.RemoveMemberFromReignAsync(target.Id.ToString());

            if (!removed)
            {
                await SendError(message, $"{target.Username} is not part of the current reign.");
                return;
            }

            await SendSuccess(message, $"🗑 {target.Username} has been removed from the reign.");

            // Officer log
            var log = _client.GetChannel(OfficerLogChannelId) as IMessageChannel;
            if (log != null)
            {
                var logEmbed = new EmbedBuilder()
                    .WithTitle("📘 Reign Update — Officer Removal")
                    .AddField("Removed", target.Username, true)
                    .AddField("By", message.Author.Username, true)
                    .WithColor(new Color(0, 110, 255))
                    .WithFooter($"{System.DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC")
                    .Build();

                await log.SendMessageAsync(embed: logEmbed);
            }
        }

        // ============================================================
        // APPLY TO REIGN
        // ============================================================

        private async Task ApplyReign(SocketMessage message)
        {
            var locked = await _reignService.GetReignLockedAsync();
            if (locked)
            {
                await SendWarning(message, "Reign is locked. Contact an officer.");
                return;
            }

            if (message.Channel.Id != VrSubmissionChannelId)
            {
                await SendError(message, $"You may only apply in <#{VrSubmissionChannelId}>.");
                return;
            }

            var fines = await _fineService.GetFinesForUserAsync(message.Author.Id.ToString());
            int activeStrikes = fines
                .Where(f => f.FineType == "Reign" && !f.IsPaid)
                .Sum(f => f.ReignStrikes);

            if (activeStrikes > 0)
            {
                await SendError(message, $"You have **{activeStrikes} Reign Strike(s)**.");
                return;
            }

            var member = await _memberService.GetMemberByDiscordIdAsync(message.Author.Id.ToString());
            if (member == null)
            {
                await SendError(message, "Please register first using `!register`.");
                return;
            }

            await _reignService.ApplyAsync(message.Author.Id.ToString());
            await SendSuccess(message, "You have been added to the Viking Reign!");
        }

        // ============================================================
        // LIST REIGN APPLICANTS
        // ============================================================

        private async Task ListReign(SocketMessage message)
        {
            var results = await _reignService.GetCurrentRegistrationsSortedAsync();
            if (results.Count == 0)
            {
                await SendInfo(message, "Viking Reign", "Nobody has applied yet.");
                return;
            }

            int pos = 1;
            string msg = "";

            foreach (var (member, reg) in results)
            {
                msg += $"**{pos})** {member.IngameName} — {member.ReignPoints} pts\n";
                pos++;
            }

            await SendInfo(message, "Viking Reign Applicants", msg);
        }

        // ============================================================
        // CLEAR REIGN (OFFICER)
        // ============================================================

        private async Task ClearReign(SocketMessage message)
        {
            if (!await IsOfficer(message)) return;

            await _reignService.ClearAsync();
            await SendSuccess(message, "Reign list cleared.");
        }

        // ============================================================
        // LOCK / UNLOCK REIGN
        // ============================================================

        private async Task LockReign(SocketMessage message)
        {
            if (!await IsOfficer(message)) return;

            await _reignService.SetReignLockedAsync(true);
            await _fineService.ReduceReignStrikesAsync();

            await SendWarning(message, "The reign is now **LOCKED**.");
        }

        private async Task UnlockReign(SocketMessage message)
        {
            if (!await IsOfficer(message)) return;

            await _reignService.SetReignLockedAsync(false);
            await SendSuccess(message, "The reign is now **UNLOCKED**.");
        }

        // ============================================================
        // EXEMPT / UNEXEMPT
        // ============================================================

        private async Task SetExempt(SocketMessage message, bool exempt)
        {
            if (!await IsOfficer(message)) return;

            if (message.MentionedUsers.Count == 0)
            {
                await SendError(message, "Usage: `!exempt @user` or `!unexempt @user`");
                return;
            }

            var target = message.MentionedUsers.First();
            var member = await _memberService.GetMemberByDiscordIdAsync(target.Id.ToString());

            if (member == null)
            {
                await SendError(message, $"{target.Username} is not registered.");
                return;
            }

            member.IsExempt = exempt;
            member.LastUpdatedUTC = System.DateTime.UtcNow;
            await _memberService.RegisterOrUpdateAsync(member);

            string status = exempt ? "EXEMPT" : "NOT EXEMPT";
            await SendSuccess(message, $"{target.Username} is now **{status}** from weekly donations.");
        }

        // ============================================================
        // PERMISSION CHECK
        // ============================================================

        private async Task<bool> IsOfficer(SocketMessage message)
        {
            if (message.Channel is not SocketGuildChannel gc)
            {
                await SendError(message, "This command must be used inside the server.");
                return false;
            }

            var user = gc.GetUser(message.Author.Id);

            if (user == null || !user.Roles.Any(r => r.Id == ReignOfficerRoleId))
            {
                await SendError(message, "You do not have permission to perform this action.");
                return false;
            }

            return true;
        }
    }
}
