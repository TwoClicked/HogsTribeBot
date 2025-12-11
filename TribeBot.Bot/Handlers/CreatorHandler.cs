using Discord;
using Discord.WebSocket;
using System.Threading.Tasks;
using TribeBot.Bot.UI; // IMPORTANT for EmbedHelper

namespace TribeBot.Bot.Handlers
{
    public class CreatorHandler
    {
        private readonly DiscordSocketClient _client;

        private const ulong CreatorRoleId = 1392919560633581728;
        private const ulong PromotionChannelId = 1440887368247939154;
        private const ulong GuildId = 1109193500664287336;

        public CreatorHandler(DiscordSocketClient client)
        {
            _client = client;
        }

        // ============================================================
        // Entry Point
        // ============================================================
        public async Task<bool> TryHandleAsync(SocketMessage message)
        {
            if (message.Author.IsBot)
                return false;

            // This handler is DM-only
            if (message.Channel is not IDMChannel)
                return false;

            if (!message.Content.StartsWith("!promote ", System.StringComparison.OrdinalIgnoreCase))
                return false;

            await HandlePromote(message);
            return true;
        }

        // ============================================================
        // !promote <YouTubeLink>
        // ============================================================
        private async Task HandlePromote(SocketMessage message)
        {
            string link = message.Content.Substring("!promote ".Length).Trim();

            if (!link.StartsWith("http"))
            {
                await message.Channel.SendMessageAsync(
                    embed: EmbedHelper.Error("Invalid link. Please provide a valid YouTube URL.")
                );
                return;
            }

            var guild = _client.GetGuild(GuildId);
            var guildUser = guild?.GetUser(message.Author.Id);

            if (guildUser == null)
            {
                await message.Channel.SendMessageAsync(
                    embed: EmbedHelper.Error("You must be in the server to use this command.")
                );
                return;
            }

            // Check role
            bool hasRole = guildUser.Roles.Any(r => r.Id == CreatorRoleId);

            if (!hasRole)
            {
                await message.Channel.SendMessageAsync(
                    embed: EmbedHelper.Error("You must have the **Content Creator** role to use this command.")
                );
                return;
            }

            // Get promo channel
            var promoChannel = _client.GetChannel(PromotionChannelId) as IMessageChannel;
            if (promoChannel == null)
            {
                await message.Channel.SendMessageAsync(
                    embed: EmbedHelper.Error("Promotion channel not found.")
                );
                return;
            }

            // PUBLIC ANNOUNCEMENT (stays plaintext on purpose)
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

            // DM Confirmation using consistent embed
            await message.Channel.SendMessageAsync(
                embed: EmbedHelper.Success("Your promotion has been posted!")
            );
        }
    }
}
