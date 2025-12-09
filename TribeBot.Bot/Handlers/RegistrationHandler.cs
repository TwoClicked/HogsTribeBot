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

        // ================================================================
        // EMBED HELPERS (local Style-2 utilities)
        // ================================================================
        private Embed BuildEmbed(string title, string desc, Color color)
        {
            return new EmbedBuilder()
                .WithTitle(title)
                .WithDescription(desc)
                .WithColor(color)
                .WithFooter($"{System.DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC")
                .Build();
        }

        private Task SendError(SocketMessage m, string text)
            => m.Channel.SendMessageAsync(embed: BuildEmbed("❌ Error", text, Color.Red));

        private Task SendWarning(SocketMessage m, string text)
            => m.Channel.SendMessageAsync(embed: BuildEmbed("⚠️ Warning", text, Color.Orange));

        private Task SendSuccess(SocketMessage m, string text)
            => m.Channel.SendMessageAsync(embed: BuildEmbed("🟢 Success", text, Color.Green));

        private Task SendInfo(SocketMessage m, string title, string text)
            => m.Channel.SendMessageAsync(embed: BuildEmbed($"🛡️ {title}", text, Color.Blue));


        // ================================================================
        // MAIN ENTRY
        // ================================================================
        public async Task<bool> TryHandleAsync(SocketMessage message)
        {
            if (message.Author.IsBot)
                return false;

            ulong userId = message.Author.Id;

            // If a flow is already active, forward the message into it
            if (_flowManager.IsInFlow(userId))
            {
                var flow = _flowManager.GetFlow(userId);
                return await flow!.HandleAsync(message);
            }

            // Detect starting command
            if (!message.Content.Equals("!register", System.StringComparison.OrdinalIgnoreCase))
                return false;

            // Must be in the server
            var guild = _client.GetGuild(GuildId);
            var user = guild?.GetUser(userId);

            if (user == null)
            {
                await SendError(message, "You must be in the server to register.");
                return true;
            }

            // Must have the HOGS role
            if (!user.Roles.Any(r => r.Id == HogsRoleId))
            {
                await SendError(message, "You do not have the **Member HOGS** role.");
                return true;
            }

            // Try DMing user to begin flow
            try
            {
                var dm = await message.Author.CreateDMChannelAsync();
                await dm.SendMessageAsync(
                    "👋 Let's get you registered!\n" +
                    "Enter your **in-game name**.\n\n" +
                    "Type **`cancel`** to stop at any time or **`back`** to return to the previous question.");
            }
            catch
            {
                await SendError(message, "I was unable to DM you. Please enable private messages and try again.");
                return true;
            }

            // Start flow
            var newFlow = new RegistrationFlow(userId, _memberService, _flowManager, _client);
            _flowManager.StartFlow(userId, newFlow);

            await SendSuccess(message, "Registration started! Please check your DMs.");

            return true;
        }
    }
}
