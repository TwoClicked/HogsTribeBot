using Discord;
using Discord.WebSocket;
using System.Threading.Tasks;

namespace TribeBot.Bot.Handlers
{
    public class FallbackHandler
    {
        private readonly DiscordSocketClient _client;

        public FallbackHandler(DiscordSocketClient client)
        {
            _client = client;
        }

        // ============================================================
        // Local Embed Builder (same as other handlers)
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

        private Task SendInfo(SocketMessage msg, string title, string text)
            => msg.Channel.SendMessageAsync(embed: BuildEmbed($"🛡️ {title}", text, Color.Blue));


        /// <summary>
        /// Called ONLY if no other handler consumed the message.
        /// </summary>
        public async Task HandleAsync(SocketMessage message)
        {
            if (message.Author.IsBot)
                return;

            // Only respond to DM fallback messages
            if (message.Channel is IDMChannel)
            {
                string info =
                    $"{message.Author.Mention}, I didn’t understand that.\n\n" +
                    "Use **!help** to see all available commands.\n" +
                    "If you still need assistance, please reach out to **BroGuruKiller** (Discord: `guru94vt`). 🐗";

                await SendInfo(message, "Unrecognized Command", info);
            }

            // Guild channel fallback = silent ignore
        }
    }
}
