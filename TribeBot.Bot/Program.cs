using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using TribeBot.Bot.Handlers;
using TribeBot.Core.Flows;
using TribeBot.Core.Flows.Interfaces;
using TribeBot.Core.Interfaces;
using TribeBot.Services;
using TribeBot.Services.Services;
using TribeBot.Data.GoogleSheets;
using TribeBot.Data.Interfaces;

namespace TribeBot.Bot
{
    public class Program
    {
        private DiscordSocketClient _client;
        private IServiceProvider _services;

        // Handlers
        private RegistrationHandler _registrationHandler;
        private UpdateHandler _updateHandler;
        private ReignHandler _reignHandler;
        private BankHandler _bankHandler;
        private FineHandler _fineHandler;
        private PollHandler _pollHandler;
        private CreatorHandler _creatorHandler;
        private HelpHandler _helpHandler;

        private GeneralHandler _generalHandler;
        private FallbackHandler _fallbackHandler;

        public static void Main(string[] args)
            => new Program().MainAsync().GetAwaiter().GetResult();

        public async Task MainAsync()
        {
            var config = new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.All
            };

            _client = new DiscordSocketClient(config);

            var services = new ServiceCollection();
            ConfigureServices(services);
            _services = services.BuildServiceProvider();

            // Build handlers from DI
            _registrationHandler = ActivatorUtilities.CreateInstance<RegistrationHandler>(_services);
            _updateHandler = ActivatorUtilities.CreateInstance<UpdateHandler>(_services);
            _reignHandler = ActivatorUtilities.CreateInstance<ReignHandler>(_services);
            _bankHandler = ActivatorUtilities.CreateInstance<BankHandler>(_services);
            _fineHandler = ActivatorUtilities.CreateInstance<FineHandler>(_services);
            _pollHandler = ActivatorUtilities.CreateInstance<PollHandler>(_services);
            _creatorHandler = ActivatorUtilities.CreateInstance<CreatorHandler>(_services);
            _helpHandler = ActivatorUtilities.CreateInstance<HelpHandler>(_services);

            _generalHandler = ActivatorUtilities.CreateInstance<GeneralHandler>(_services);
            _fallbackHandler = ActivatorUtilities.CreateInstance<FallbackHandler>(_services);

            _client.Log += LogAsync;
            _client.MessageReceived += MessageReceivedAsync;

            string token = Environment.GetEnvironmentVariable("DISCORD_TOKEN");
            if (string.IsNullOrWhiteSpace(token))
            {
                Console.WriteLine("ERROR: DISCORD_TOKEN missing.");
                return;
            }

            await _client.LoginAsync(TokenType.Bot, token);
            await _client.StartAsync();

            Console.WriteLine("Bot is running...");
            await Task.Delay(-1);
        }

        private void ConfigureServices(IServiceCollection services)
        {
            // Flow System
            services.AddSingleton<IUserFlowManager, UserFlowManager>();

            // Data Layer
            services.AddSingleton<IGoogleSheetsDataStore>(provider =>
                new GoogleSheetsDataStore(
                    @"C:\Users\diego\source\repos\HogsTribeBot\credentials.json",
                    "1O_bpIDhAApw00-yj6uwKt1KPPrswc0w6tyejmwSS-Xk"
                ));

            // OCR
            services.AddSingleton(new PaddleOcrServerService(
                @"C:\PaddleOCR\PaddleOCR-json_v1.4.1\PaddleOCR-json.exe"));

            // Domain Services
            services.AddSingleton<IMemberService, MemberService>();
            services.AddSingleton<IDonationService, DonationService>();
            services.AddSingleton<IFineService, FineService>();
            services.AddSingleton<IReignService, ReignService>();
            services.AddSingleton<IVoteService, VoteService>();

            // Discord Client
            services.AddSingleton(_client);
        }

        private Task LogAsync(LogMessage msg)
        {
            Console.WriteLine(msg.ToString());
            return Task.CompletedTask;
        }

        // Clean dispatcher
        private async Task MessageReceivedAsync(SocketMessage message)
        {
            if (message.Author.IsBot)
                return;

            // Order of handlers matters:
            if (await _registrationHandler.TryHandleAsync(message)) return;
            if (await _updateHandler.TryHandleAsync(message)) return;
            if (await _reignHandler.TryHandleAsync(message)) return;
            if (await _bankHandler.TryHandleAsync(message)) return;
            if (await _fineHandler.TryHandleAsync(message)) return;
            if (await _pollHandler.TryHandleAsync(message)) return;
            if (await _creatorHandler.TryHandleAsync(message)) return;
            if (await _helpHandler.TryHandleAsync(message)) return;
            if (await _generalHandler.TryHandleAsync(message)) return;

            // If nothing consumed it:
            await _fallbackHandler.HandleAsync(message);
        }
    }
}
