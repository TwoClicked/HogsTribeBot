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

        /// <summary>
        /// Called ONLY if no other handler consumed the message.
        /// </summary>
        public async Task HandleAsync(SocketMessage message)
        {
            // Ignore bot messages
            if (message.Author.IsBot)
                return;

            // Only respond to DM fallback messages
            if (message.Channel is IDMChannel)
            {
                await message.Channel.SendMessageAsync(
                    $"{message.Author.Mention} I didn’t understand that.\n" +
                    "Use **!help** to see all available commands.\n" +
                    "If you still need assistance, message BroGuruKiller, Discord tag guru94vt. 🐗");
            }

            // If it's a guild channel: ignore silently
        }
    }
}
