using Discord;
using Discord.WebSocket;
using System.Linq;
using System.Threading.Tasks;
using TribeBot.Core.Interfaces;

namespace TribeBot.Bot.Handlers
{
    public class UpdateHandler
    {
        private readonly DiscordSocketClient _client;
        private readonly IMemberService _memberService;

        public UpdateHandler(
            DiscordSocketClient client,
            IMemberService memberService)
        {
            _client = client;
            _memberService = memberService;
        }

        // ============================================================
        // EMBED HELPERS (Style 2)
        // ============================================================
        private Embed Build(string title, string desc, Color color)
            => new EmbedBuilder()
                .WithTitle(title)
                .WithDescription(desc)
                .WithColor(color)
                .WithFooter($"{System.DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC")
                .Build();

        private Task Success(SocketMessage m, string t)
            => m.Channel.SendMessageAsync(embed: Build("🟢 Success", t, Color.Green));

        private Task Error(SocketMessage m, string t)
            => m.Channel.SendMessageAsync(embed: Build("❌ Error", t, Color.Red));

        private Task Warning(SocketMessage m, string t)
            => m.Channel.SendMessageAsync(embed: Build("⚠️ Warning", t, Color.Orange));

        private Task Info(SocketMessage m, string title, string body)
            => m.Channel.SendMessageAsync(embed: Build($"🛡️ {title}", body, Color.Blue));


        // ============================================================
        // ENTRY POINT
        // ============================================================
        public async Task<bool> TryHandleAsync(SocketMessage message)
        {
            if (message.Author.IsBot)
                return false;

            string content = message.Content.Trim().ToLower();

            if (content.StartsWith("!updateigname"))
            {
                await UpdateName(message);
                return true;
            }
            if (content.StartsWith("!updateid"))
            {
                await UpdateId(message);
                return true;
            }
            if (content.StartsWith("!updatemight"))
            {
                await UpdateMight(message);
                return true;
            }
            if (content.StartsWith("!updatekills"))
            {
                await UpdateKills(message);
                return true;
            }
            if (content.StartsWith("!updatecollector"))
            {
                await UpdateCollector(message);
                return true;
            }
            if (content.StartsWith("!updatereignpoints"))
            {
                await UpdateReignPoints(message);
                return true;
            }

            return false;
        }

        // ============================================================
        // OFFICER-ONLY: UPDATE REIGN POINTS
        // ============================================================
        private async Task UpdateReignPoints(SocketMessage message)
        {
            var user = message.Author as SocketGuildUser;
            ulong officerRoleId = 1222665812775534592;

            if (user == null || !user.Roles.Any(r => r.Id == officerRoleId))
            {
                await Error(message, "Only Officers may use this command.");
                return;
            }

            var mentioned = message.MentionedUsers.FirstOrDefault();
            if (mentioned == null)
            {
                await Warning(message, "Usage: `!updateReignPoints @user <points>`");
                return;
            }

            string[] parts = message.Content.Split(" ");
            if (parts.Length < 3 || !long.TryParse(parts.Last(), out long points) || points < 0)
            {
                await Error(message, "Invalid points. Example: `!updateReignPoints @user 12345`");
                return;
            }

            var member = await _memberService.GetMemberByDiscordIdAsync(mentioned.Id.ToString());
            if (member == null)
            {
                await Error(message, "That user is not registered.");
                return;
            }

            member.ReignPoints = points;
            member.LastUpdatedUTC = System.DateTime.UtcNow;
            await _memberService.RegisterOrUpdateAsync(member);

            await Success(message,
                $"Updated **{member.IngameName}**’s Reign Points to **{points:N0}**.");
        }

        // ============================================================
        // UPDATE IGN NAME
        // ============================================================
        private async Task UpdateName(SocketMessage message)
        {
            var member = await _memberService.GetMemberByDiscordIdAsync(message.Author.Id.ToString());

            if (member == null)
            {
                await Error(message, "You must register first using `!register`.");
                return;
            }

            string newName = message.Content.Substring("!updateigname ".Length).Trim();

            if (string.IsNullOrWhiteSpace(newName) ||
                newName.Length > 20 ||
                !System.Text.RegularExpressions.Regex.IsMatch(newName, @"^[A-Za-z0-9 ]+$"))
            {
                await Error(message, "Invalid name. Only letters, numbers, and spaces allowed (max 20 characters).");
                return;
            }

            member.IngameName = newName;
            member.LastUpdatedUTC = System.DateTime.UtcNow;
            await _memberService.RegisterOrUpdateAsync(member);

            await Success(message, $"Your in-game name has been updated to **{newName}**.");
        }

        // ============================================================
        // UPDATE IN-GAME ID
        // ============================================================
        private async Task UpdateId(SocketMessage message)
        {
            var member = await _memberService.GetMemberByDiscordIdAsync(message.Author.Id.ToString());

            if (member == null)
            {
                await Error(message, "You must register first using `!register`.");
                return;
            }

            string input = message.Content.Substring("!updateid ".Length).Trim();

            if (!long.TryParse(input, out long id) || id < 1 || id > 9999999999)
            {
                await Error(message, "Invalid ID. Must be 1–10 digits.");
                return;
            }

            var all = await _memberService.GetAllMembersAsync();
            if (all.Any(m => m.IngameId == input && m.DiscordUserId != member.DiscordUserId))
            {
                await Error(message, "That ID already belongs to another member.");
                return;
            }

            member.IngameId = input;
            member.LastUpdatedUTC = System.DateTime.UtcNow;
            await _memberService.RegisterOrUpdateAsync(member);

            await Success(message, $"Your ID has been updated to `{input}`.");
        }

        // ============================================================
        // UPDATE MIGHT
        // ============================================================
        private async Task UpdateMight(SocketMessage message)
        {
            var member = await _memberService.GetMemberByDiscordIdAsync(message.Author.Id.ToString());

            if (member == null)
            {
                await Error(message, "You must register first using `!register`.");
                return;
            }

            string input = message.Content.Substring("!updatemight ".Length).Trim();

            if (!long.TryParse(input, out long might) || might < 0 || might > 3000000000)
            {
                await Error(message, "Invalid Might. Must be between 0 and 3,000,000,000.");
                return;
            }

            member.Might = (int)might;
            member.LastUpdatedUTC = System.DateTime.UtcNow;
            await _memberService.RegisterOrUpdateAsync(member);

            await Success(message, $"Your Might has been updated to **{might:N0}**.");
        }

        // ============================================================
        // UPDATE KILLS
        // ============================================================
        private async Task UpdateKills(SocketMessage message)
        {
            var member = await _memberService.GetMemberByDiscordIdAsync(message.Author.Id.ToString());

            if (member == null)
            {
                await Error(message, "You must register first using `!register`.");
                return;
            }

            string input = message.Content.Substring("!updatekills ".Length).Trim();

            if (!long.TryParse(input, out long kills) || kills < 0 || kills > 500000000000)
            {
                await Error(message, "Invalid Kill Points.");
                return;
            }

            member.KillPoints = kills;
            member.LastUpdatedUTC = System.DateTime.UtcNow;
            await _memberService.RegisterOrUpdateAsync(member);

            await Success(message, $"Your Kill Points have been updated to **{kills:N0}**.");
        }

        // ============================================================
        // UPDATE COLLECTOR LEVEL
        // ============================================================
        private async Task UpdateCollector(SocketMessage message)
        {
            var member = await _memberService.GetMemberByDiscordIdAsync(message.Author.Id.ToString());

            if (member == null)
            {
                await Error(message, "You must register first using `!register`.");
                return;
            }

            string input = message.Content.Substring("!updatecollector ".Length).Trim();

            if (!int.TryParse(input, out int lvl) || lvl < 0 || lvl > 100)
            {
                await Error(message, "Invalid Collector Level. Must be 0–100.");
                return;
            }

            member.CollectorLevel = lvl;
            member.LastUpdatedUTC = System.DateTime.UtcNow;
            await _memberService.RegisterOrUpdateAsync(member);

            await Success(message, $"Your Collector Level has been updated to **{lvl}**.");
        }
    }
}
