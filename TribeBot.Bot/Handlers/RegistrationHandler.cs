using Discord;
using Discord.WebSocket;
using System.Threading.Tasks;
using TribeBot.Core.Flows.Interfaces;
using TribeBot.Core.Flows;
using TribeBot.Core.Interfaces;

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

        public async Task<bool> TryHandleAsync(SocketMessage message)
        {
            if (message.Author.IsBot)
                return false;

            ulong userId = message.Author.Id;

            // If user is inside a flow, forward message to that flow
            if (_flowManager.IsInFlow(userId))
            {
                var flow = _flowManager.GetFlow(userId);
                return await flow!.HandleAsync(message);
            }

            // Detect command
            if (!message.Content.Equals("!register", System.StringComparison.OrdinalIgnoreCase))
                return false;

            // Must occur in server or DM? Original behavior allowed use anywhere and bot DM’d them.
            var guild = _client.GetGuild(GuildId);
            var user = guild?.GetUser(userId);

            if (user == null)
            {
                await message.Channel.SendMessageAsync("❌ You must be in the server to register.");
                return true;
            }

            // Check role
            if (!user.Roles.Any(r => r.Id == HogsRoleId))
            {
                await message.Channel.SendMessageAsync("❌ You do not have the **Member HOGS** role.");
                return true;
            }

            // Attempt DM
            try
            {
                var dm = await message.Author.CreateDMChannelAsync();
                await dm.SendMessageAsync(
                    "👋 Let's get you registered!\n" +
                    "Enter your **In-game name** (type `cancel` to stop at any time).");
            }
            catch
            {
                await message.Channel.SendMessageAsync($"{message.Author.Mention} ❌ I couldn't DM you.");
                return true;
            }

            // Start flow
            var newFlow = new RegistrationFlow(userId, _memberService, _flowManager);
            _flowManager.StartFlow(userId, newFlow);

            return true;
        }
    }
}
