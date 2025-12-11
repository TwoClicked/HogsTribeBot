using Discord;
using Discord.WebSocket;
using System.Threading.Tasks;
using TribeBot.Core.Flows.Interfaces;
using TribeBot.Core.Flows;
using TribeBot.Core.Interfaces;
using TribeBot.Bot.UI; // <-- IMPORTANT for EmbedHelper

namespace TribeBot.Bot.Handlers
{
    public class RegistrationHandler
    {
        private readonly DiscordSocketClient _client;
        private readonly IUserFlowManager _flowManager;
        private readonly IMemberService _memberService;

        private const ulong HogsRoleId = 1222668156271591485;
        private const ulong GuildId = 1109193500664287336;

        public RegistrationHandler(
            DiscordSocketClient client,
            IUserFlowManager flowManager,
            IMemberService memberService)
        {
            _client = client;
            _flowManager = flowManager;
            _memberService = memberService;
        }

        // ================================================================
        // MAIN ENTRY
        // ================================================================
        public async Task<bool> TryHandleAsync(SocketMessage message)
        {
            if (message.Author.IsBot)
                return false;

            ulong userId = message.Author.Id;

            // If user is already mid-flow, send message into that flow
            if (_flowManager.IsInFlow(userId))
            {
                var flow = _flowManager.GetFlow(userId);
                return await flow!.HandleAsync(message);
            }

            // Detect start command
            if (!message.Content.Equals("!register", System.StringComparison.OrdinalIgnoreCase))
                return false;

            // Must be in the server
            var guild = _client.GetGuild(GuildId);
            var user = guild?.GetUser(userId);

            if (user == null)
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error("You must be in the server to register."));
                return true;
            }

            // Must have required role
            if (!user.Roles.Any(r => r.Id == HogsRoleId))
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error("You do not have the **Member HOGS** role."));
                return true;
            }

            // Attempt to DM user to begin flow
            try
            {
                var dm = await message.Author.CreateDMChannelAsync();
                await dm.SendMessageAsync(
                    "👋 Let's get you registered!\n" +
                    "Enter your **in-game name**.\n\n" +
                    "Type **`cancel`** to stop at any time, or **`back`** to return to previous questions."
                );
            }
            catch
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error("I was unable to DM you. Please enable private messages and try again."));
                return true;
            }

            // Start the registration flow
            var newFlow = new RegistrationFlow(userId, _memberService, _flowManager, _client);
            _flowManager.StartFlow(userId, newFlow);

            await message.Channel.SendMessageAsync(embed:
                EmbedHelper.Success("Registration started! Please check your DMs."));

            return true;
        }
    }
}
