using Discord;
using Discord.WebSocket;
using System.Threading.Tasks;
using TribeBot.Bot.UI; // IMPORTANT for EmbedHelper

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
            if (message.Author.IsBot)
                return;

            // Only reply in DMs to avoid cluttering guild channels
            if (message.Channel is IDMChannel)
            {
                string info =
                    $"{message.Author.Mention}, I didn’t understand that.\n\n" +
                    "Use **!help** to see all available commands.\n" +
                    "If you still need assistance, reach out to **BroGuruKiller** (`guru94vt`). 🐗";

                await message.Channel.SendMessageAsync(
                    embed: EmbedHelper.Info("Unrecognized Command", info));
            }

            // In guild channels: remain silent
        }
    }
}
