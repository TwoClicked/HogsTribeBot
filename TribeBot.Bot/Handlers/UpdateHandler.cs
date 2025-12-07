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

        public async Task<bool> TryHandleAsync(SocketMessage message)
        {
            if (message.Author.IsBot)
                return false;

            string content = message.Content.Trim();

            // Simple updates
            if (content.StartsWith("!updateigname ", System.StringComparison.OrdinalIgnoreCase))
            {
                await UpdateName(message);
                return true;
            }

            if (content.StartsWith("!updateid ", System.StringComparison.OrdinalIgnoreCase))
            {
                await UpdateId(message);
                return true;
            }

            if (content.StartsWith("!updatemight ", System.StringComparison.OrdinalIgnoreCase))
            {
                await UpdateMight(message);
                return true;
            }

            if (content.StartsWith("!updatekills ", System.StringComparison.OrdinalIgnoreCase))
            {
                await UpdateKills(message);
                return true;
            }

            if (content.StartsWith("!updatecollector ", System.StringComparison.OrdinalIgnoreCase))
            {
                await UpdateCollector(message);
                return true;
            }

            return false;
        }

        // ============================================================
        //  UPDATE COMMAND IMPLEMENTATIONS
        // ============================================================

        private async Task UpdateName(SocketMessage message)
        {
            var member = await _memberService.GetMemberByDiscordIdAsync(message.Author.Id.ToString());
            if (member == null)
            {
                await message.Channel.SendMessageAsync($"{message.Author.Mention} ❌ You must register first.");
                return;
            }

            string newName = message.Content.Substring("!updateigname ".Length).Trim();

            if (string.IsNullOrWhiteSpace(newName) ||
                newName.Length > 20 ||
                !System.Text.RegularExpressions.Regex.IsMatch(newName, @"^[A-Za-z0-9 ]+$"))
            {
                await message.Channel.SendMessageAsync("❌ Invalid name. Only letters, numbers, and spaces (max 20).");
                return;
            }

            member.IngameName = newName;
            member.LastUpdatedUTC = System.DateTime.UtcNow;

            await _memberService.RegisterOrUpdateAsync(member);
            await message.Channel.SendMessageAsync($"✔ Updated name to **{newName}**.");
        }

        private async Task UpdateId(SocketMessage message)
        {
            var member = await _memberService.GetMemberByDiscordIdAsync(message.Author.Id.ToString());
            if (member == null)
            {
                await message.Channel.SendMessageAsync($"{message.Author.Mention} ❌ You must register first.");
                return;
            }

            string input = message.Content.Substring("!updateid ".Length).Trim();

            if (!long.TryParse(input, out long id) || id < 1 || id > 9999999999)
            {
                await message.Channel.SendMessageAsync("❌ Invalid ID. Must be 1–10 digits.");
                return;
            }

            // Check duplicates (but allow own)
            var all = await _memberService.GetAllMembersAsync();
            if (all.Any(m => m.IngameId == input && m.DiscordUserId != member.DiscordUserId))
            {
                await message.Channel.SendMessageAsync("❌ That ID belongs to another member.");
                return;
            }

            member.IngameId = input;
            member.LastUpdatedUTC = System.DateTime.UtcNow;

            await _memberService.RegisterOrUpdateAsync(member);
            await message.Channel.SendMessageAsync($"✔ Updated ID to `{input}`.");
        }

        private async Task UpdateMight(SocketMessage message)
        {
            var member = await _memberService.GetMemberByDiscordIdAsync(message.Author.Id.ToString());
            if (member == null)
            {
                await message.Channel.SendMessageAsync("❌ You must register first.");
                return;
            }

            string input = message.Content.Substring("!updatemight ".Length).Trim();

            if (!long.TryParse(input, out long might) || might < 0 || might > 3000000000)
            {
                await message.Channel.SendMessageAsync("❌ Invalid Might. Must be between 0 and 3000000000.");
                return;
            }

            member.Might = (int)might;
            member.LastUpdatedUTC = System.DateTime.UtcNow;

            await _memberService.RegisterOrUpdateAsync(member);
            await message.Channel.SendMessageAsync($"✔ Might updated to **{might:N0}**.");
        }

        private async Task UpdateKills(SocketMessage message)
        {
            var member = await _memberService.GetMemberByDiscordIdAsync(message.Author.Id.ToString());
            if (member == null)
            {
                await message.Channel.SendMessageAsync("❌ You must register first.");
                return;
            }

            string input = message.Content.Substring("!updatekills ".Length).Trim();

            if (!long.TryParse(input, out long kills) || kills < 0 || kills > 500000000000)
            {
                await message.Channel.SendMessageAsync("❌ Invalid Kill Points.");
                return;
            }

            member.KillPoints = kills;
            member.LastUpdatedUTC = System.DateTime.UtcNow;

            await _memberService.RegisterOrUpdateAsync(member);
            await message.Channel.SendMessageAsync($"✔ Kill Points updated to **{kills:N0}**.");
        }

        private async Task UpdateCollector(SocketMessage message)
        {
            var member = await _memberService.GetMemberByDiscordIdAsync(message.Author.Id.ToString());
            if (member == null)
            {
                await message.Channel.SendMessageAsync("❌ You must register first.");
                return;
            }

            string input = message.Content.Substring("!updatecollector ".Length).Trim();

            if (!int.TryParse(input, out int lvl) || lvl < 0 || lvl > 100)
            {
                await message.Channel.SendMessageAsync("❌ Invalid Collector Level. Must be 0–100.");
                return;
            }

            member.CollectorLevel = lvl;
            member.LastUpdatedUTC = System.DateTime.UtcNow;

            await _memberService.RegisterOrUpdateAsync(member);
            await message.Channel.SendMessageAsync($"✔ Collector Level updated to **{lvl}**.");
        }
    }
}
