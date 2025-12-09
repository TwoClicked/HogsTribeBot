using Discord;
using Discord.WebSocket;
using System.Threading.Tasks;

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
        // Embed Helpers (Local for this handler)
        // ============================================================

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

        private Task SendInfo(SocketMessage msg, string title, string text)
            => msg.Channel.SendMessageAsync(embed: BuildEmbed($"🛡️ {title}", text, Color.Blue));


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

            if (!message.Content.StartsWith("!promote ", StringComparison.OrdinalIgnoreCase))
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
                await SendError(message, "Invalid link. Please provide a valid YouTube URL.");
                return;
            }

            var guild = _client.GetGuild(GuildId);
            var guildUser = guild?.GetUser(message.Author.Id);

            if (guildUser == null)
            {
                await SendError(message, "You must be in the server to use this command.");
                return;
            }

            // Check role
            bool hasRole = guildUser.Roles.Any(r => r.Id == CreatorRoleId);

            if (!hasRole)
            {
                await SendError(message, "You must have the **Content Creator** role to use this command.");
                return;
            }

            // Get promo channel
            var promoChannel = _client.GetChannel(PromotionChannelId) as IMessageChannel;
            if (promoChannel == null)
            {
                await SendError(message, "Promotion channel not found.");
                return;
            }

            // Post the announcement (PUBLIC MESSAGE SHOULD REMAIN TEXT)
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

            // DM Confirmation
            await SendSuccess(message, "Your promotion has been posted!");
        }
    }
}
