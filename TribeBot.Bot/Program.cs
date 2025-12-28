using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using TribeBot.Bot.Handlers;
using TribeBot.Bot.Services;
using TribeBot.Core.Flows;
using TribeBot.Core.Flows.Interfaces;
using TribeBot.Core.Interfaces;
using TribeBot.Data.GoogleSheets;
using TribeBot.Data.Interfaces;
using TribeBot.Services;
using TribeBot.Services.Services;

namespace TribeBot.Bot
{
    public class Program
    {
        private DiscordSocketClient _client;
        private IServiceProvider _services;
        private InteractionService _interactionService;

        // Message-based handlers — KEEP ORDER
        private RegistrationHandler _registrationHandler;
        private ReignHandler _reignHandler;
        private BankHandler _bankHandler;
        private FineHandler _fineHandler;
        private PollHandler _poll_handler;
        private CreatorHandler _creator_handler;
        private HelpHandler _help_handler;
        private GeneralHandler _general_handler;
        private FallbackHandler _fallback_handler;
        private bool _bankAuditStarted;


        public static void Main(string[] args)
            => new Program().MainAsync().GetAwaiter().GetResult();

        public async Task MainAsync()
        {
            var config = new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.All,
                UseInteractionSnowflakeDate = false
            };

            _client = new DiscordSocketClient(config);

            // SETUP DI
            var services = new ServiceCollection();
            ConfigureServices(services);
            _services = services.BuildServiceProvider();

            // Instantiate message handlers — KEEP ORDER
            _registrationHandler = ActivatorUtilities.CreateInstance<RegistrationHandler>(_services);
            _reignHandler = ActivatorUtilities.CreateInstance<ReignHandler>(_services);
            _bankHandler = ActivatorUtilities.CreateInstance<BankHandler>(_services);
            _fineHandler = ActivatorUtilities.CreateInstance<FineHandler>(_services);
            _poll_handler = ActivatorUtilities.CreateInstance<PollHandler>(_services);
            _creator_handler = ActivatorUtilities.CreateInstance<CreatorHandler>(_services);
            _help_handler = ActivatorUtilities.CreateInstance<HelpHandler>(_services);
            _general_handler = ActivatorUtilities.CreateInstance<GeneralHandler>(_services);
            _fallback_handler = ActivatorUtilities.CreateInstance<FallbackHandler>(_services);

            _client.Log += LogAsync;
            _client.MessageReceived += MessageReceivedAsync;
            _client.Ready += ReadyAsync;

            string token = Environment.GetEnvironmentVariable("DISCORD_TOKEN");
            if (string.IsNullOrWhiteSpace(token))
            {
                Console.WriteLine("ERROR: DISCORD_TOKEN missing.");
                return;
            }

            await _client.LoginAsync(TokenType.Bot, token);
            await _client.StartAsync();

            Console.WriteLine("Bot launched successfully.");
            await Task.Delay(-1);
        }

        // =====================================================================
        // DI CONFIGURATION — INTERACTION MODULE FIX APPLIED
        // =====================================================================
        private void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<IUserFlowManager, UserFlowManager>();

            services.AddSingleton<IGoogleSheetsDataStore>(provider =>
                new GoogleSheetsDataStore(
                    @"C:\Users\diego\source\repos\HogsTribeBot\credentials.json",
                    "1O_bpIDhAApw00-yj6uwKt1KPPrswc0w6tyejmwSS-Xk"
                ));

            services.AddSingleton(new PaddleOcrServerService(
                @"C:\PaddleOCR\PaddleOCR-json_v1.4.1\PaddleOCR-json.exe"));

            services.AddSingleton<IMemberService, MemberService>();
            services.AddSingleton<IDonationService, DonationService>();
            services.AddSingleton<IFineService, FineService>();
            services.AddSingleton<IReignService, ReignService>();
            services.AddSingleton<IVoteService, VoteService>();
            services.AddSingleton<IFarmTribeService, FarmTribeService>();
            services.AddSingleton<IFarmService, FarmService>();
            services.AddSingleton<IFarmTribeAssignmentService, FarmTribeAssignmentService>();


            services.AddSingleton<InteractionService>(provider =>
                new InteractionService(provider.GetRequiredService<DiscordSocketClient>()));

            services.AddSingleton<SchedulerService>();

            // ❌ DO NOT REGISTER INTERACTION MODULES HERE
            // These lines broke your modals:
            //
            // services.AddSingleton<ScheduledEventHandler>();
            // services.AddSingleton<EventsManagementHandler>();
            // services.AddSingleton<UpdateHandler>();
            // services.AddSingleton<EventNotifyRoleHandler>();
            //
            // Interaction modules are loaded ONLY via InteractionService.AddModulesAsync

            services.AddSingleton(_client);
        }

        // =====================================================================
        // READY EVENT — LOAD MODULES + REGISTER SLASH COMMANDS
        // =====================================================================
        private async Task ReadyAsync()
        {
            try
            {


                _interactionService = _services.GetRequiredService<InteractionService>();

                Console.WriteLine("[Init] Loading interaction modules...");
                await _interactionService.AddModulesAsync(typeof(Program).Assembly, _services);

                Console.WriteLine("[Init] Syncing guild slash commands...");
                await _interactionService.RegisterCommandsToGuildAsync(
                    guildId: 1109193500664287336,
                    deleteMissing: true
                );

                Console.WriteLine("[Init] Slash commands synced.");

                _client.InteractionCreated += HandleInteractionAsync;


                // =====================================================
                // WEEKLY BANK PRE-RESET AUDIT
                // =====================================================
                if (!_bankAuditStarted)
                {
                    _bankAuditStarted = true;

                    _ = Task.Run(async () =>
                    {
                        while (true)
                        {
                            var delay = GetDelayUntilPreReset();
                            await Task.Delay(delay);

                            try
                            {
                                await _bankHandler.LogUnpaidBeforeResetAsync();
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[Bank Audit Error] {ex}");
                            }

                            // Sleep past reset so it never fires twice
                            await Task.Delay(TimeSpan.FromHours(2));
                        }
                    });
                }

                _services.GetRequiredService<SchedulerService>().Start();

                Console.WriteLine("[Init] Bot Ready.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Ready Error] {ex}");
            }
        }

        // =====================================================================
        // INTERACTION DISPATCHER
        // =====================================================================
        private async Task HandleInteractionAsync(SocketInteraction interaction)
        {
            try
            {
                Console.WriteLine($"[INTERACTION] Type={interaction.Type} ID={interaction.Id}");

                if (interaction is SocketModal modal)
                {
                    Console.WriteLine($"[MODAL SUBMIT] CustomId = {modal.Data.CustomId}");
                }

                var ctx = new SocketInteractionContext(_client, interaction);
                await _interactionService.ExecuteCommandAsync(ctx, _services);

            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Interaction Error] {ex}");
            }
        }

        // =====================================================================
        // MESSAGE HANDLER — KEEP ORDER
        // =====================================================================
        private async Task MessageReceivedAsync(SocketMessage message)
        {
            if (message.Author.IsBot || message.Source != MessageSource.User)
                return;

            if (await _registrationHandler.TryHandleAsync(message)) return;
            if (await _reignHandler.TryHandleAsync(message)) return;
            if (await _bankHandler.TryHandleAsync(message)) return;
            if (await _fineHandler.TryHandleAsync(message)) return;
            if (await _poll_handler.TryHandleAsync(message)) return;
            if (await _creator_handler.TryHandleAsync(message)) return;
            if (await _help_handler.TryHandleAsync(message)) return;
            if (await _general_handler.TryHandleAsync(message)) return;


            await _fallback_handler.HandleAsync(message);
        }

        // =====================================================================
        // unpaid BANK helper
        // =====================================================================
        private static TimeSpan GetDelayUntilPreReset()
        {
            var now = DateTime.UtcNow;

            // Example: Sunday 1 hour before weekly reset at 00:00 UTC
            var nextReset = now.Date.AddDays((7 - (int)now.DayOfWeek) % 7);
            var auditTime = nextReset.AddHours(-1);

            if (auditTime <= now)
                auditTime = auditTime.AddDays(7);

            return auditTime - now;
        }


        // =====================================================================
        // LOGGING
        // =====================================================================
        private Task LogAsync(LogMessage msg)
        {
            Console.WriteLine(msg.ToString());
            return Task.CompletedTask;
        }
    }
}
