using Discord;
using Discord.WebSocket;
using System.Threading.Tasks;

namespace TribeBot.Bot.Handlers
{
    public class CreatorHandler
    {
        private readonly DiscordSocketClient _client;

        // CONFIG: These IDs come from your original program
        private const ulong CreatorRoleId = 1392919560633581728;
        private const ulong PromotionChannelId = 1440887368247939154;
        private const ulong GuildId = 1109193500664287336;

        public CreatorHandler(DiscordSocketClient client)
        {
            _client = client;
        }

        public async Task<bool> TryHandleAsync(SocketMessage message)
        {
            if (message.Author.IsBot)
                return false;

            // Only handle DM usage
            if (message.Channel is not IDMChannel)
                return false;

            if (!message.Content.StartsWith("!promote ", System.StringComparison.OrdinalIgnoreCase))
                return false;

            await HandlePromote(message);
            return true;
        }

        // ======================================================================
        // !promote <YouTubeLink>
        // ======================================================================
        private async Task HandlePromote(SocketMessage message)
        {
            string link = message.Content.Substring("!promote ".Length).Trim();

            if (!link.StartsWith("http"))
            {
                await message.Channel.SendMessageAsync("❌ Invalid link. Please provide a valid YouTube URL.");
                return;
            }

            var guild = _client.GetGuild(GuildId);
            var guildUser = guild?.GetUser(message.Author.Id);

            if (guildUser == null)
            {
                await message.Channel.SendMessageAsync("❌ You must be in the server to use this.");
                return;
            }

            // Check content creator role
            bool hasRole = guildUser.Roles.Any(r => r.Id == CreatorRoleId);

            if (!hasRole)
            {
                await message.Channel.SendMessageAsync("❌ You must have the **Content Creator** role to use this command.");
                return;
            }

            // Get promotion channel
            var promoChannel = _client.GetChannel(PromotionChannelId) as IMessageChannel;
            if (promoChannel == null)
            {
                await message.Channel.SendMessageAsync("❌ Promotion channel not found.");
                return;
            }

            // Send promotion announcement
            await promoChannel.SendMessageAsync(
                "@everyone\n" +
                $"📣 **New Video Drop!**\n\n" +
                $"🔥 Our HOGS Content Creator {guildUser.Mention} just uploaded a new video!\n\n" +
                $"👉 **{link}**\n\n" +
                "👍 Like the video\n" +
                "💬 Leave a comment\n" +
                "🔔 Subscribe to show support!\n\n" +
                "Let’s show them some love! 🐗💥"
            );

            // Confirm to creator privately
            await message.Channel.SendMessageAsync("✅ Your promotion has been posted!");
        }
    }
}
