using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TribeBot.Data.GoogleSheets;
using TribeBot.Data.Interfaces;
using TribeBot.Services.Services;
using TribeBot.Services.Interfaces;
using TribeBot.Core.Entities;

namespace TribeBot.Bot
{
    class Program
    {
        private DiscordSocketClient _client;
        private IServiceProvider _services;

        // Track ongoing registration sessions
        private Dictionary<ulong, RegistrationSession> _registrationSessions = new();

        static void Main(string[] args)
            => new Program().MainAsync().GetAwaiter().GetResult();

        public async Task MainAsync()
        {
            var config = new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.All
            };

            _client = new DiscordSocketClient(config);

            var serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection);
            _services = serviceCollection.BuildServiceProvider();

            _client.Log += LogAsync;
            _client.Ready += ReadyAsync;
            _client.MessageReceived += MessageReceivedAsync;

            string token = Environment.GetEnvironmentVariable("DISCORD_TOKEN");

            if (string.IsNullOrWhiteSpace(token))
            {
                Console.WriteLine("ERROR: DISCORD_TOKEN environment variable is not set.");
                return;
            }

            await _client.LoginAsync(TokenType.Bot, token);
            await _client.StartAsync();

            Console.WriteLine("Bot is running... Press Ctrl+C to stop.");
            await Task.Delay(-1);
        }

        private void ConfigureServices(ServiceCollection services)
        {
            string credentialsPath = @"C:\Users\diego\source\repos\HogsTribeBot\credentials.json";
            string spreadsheetId = "1O_bpIDhAApw00-yj6uwKt1KPPrswc0w6tyejmwSS-Xk";

            // Google Sheets datastore
            services.AddSingleton<IGoogleSheetsDataStore>(provider =>
                new GoogleSheetsDataStore(credentialsPath, spreadsheetId));

            // Services
            services.AddSingleton<IMemberService, MemberService>();
            services.AddSingleton<IReignService, ReignService>();
            services.AddSingleton<IDonationService, DonationService>();

            // Discord client
            services.AddSingleton(_client);
        }

        private Task LogAsync(LogMessage msg)
        {
            Console.WriteLine(msg.ToString());
            return Task.CompletedTask;
        }

        private Task ReadyAsync()
        {
            Console.WriteLine($"Connected as {_client.CurrentUser}");
            return Task.CompletedTask;
        }

        private async Task MessageReceivedAsync(SocketMessage message)
        {
            if (message.Author.IsBot) return;

            bool isDM = message.Channel is IDMChannel;
            ulong userId = message.Author.Id;

            // ============================
            // START REGISTRATION FLOW
            // ============================

            if (message.Content.Equals("!register", StringComparison.OrdinalIgnoreCase))
            {

                ulong guildId = 1109193500664287336;
                var guild = _client.GetGuild(guildId);
                var guildUser = guild?.GetUser(userId);

                if (guildUser == null)
                {
                    await message.Channel.SendMessageAsync("You must be a member of the server to register.");
                    return;
                }

                bool hasRole = guildUser.Roles.Any(r => r.Id == 1222668156271591485);

                if (!hasRole)
                {
                    await message.Channel.SendMessageAsync("You do not have the **Member HOGS** role. You cannot register.");
                    return;
                }

                try
                {
                    var dm = await message.Author.CreateDMChannelAsync();
                    await dm.SendMessageAsync("Let's get you registered! 😊\n\nWhat is your **in-game name**?\nExample: `OneClick`");
                }
                catch
                {
                    await message.Channel.SendMessageAsync($"{message.Author.Mention} I couldn't DM you. Please enable DMs and try again.");
                    return;
                }

                _registrationSessions[userId] = new RegistrationSession
                {
                    CurrentStep = RegistrationSession.Step.AskIngameName
                };

                return;
            }

            // Continue registration (DM only)
            if (isDM && _registrationSessions.ContainsKey(userId))
            {
                var session = _registrationSessions[userId];
                var memberService = _services.GetService<IMemberService>();

                switch (session.CurrentStep)
                {
                    case RegistrationSession.Step.AskIngameName:
                        session.IngameName = message.Content.Trim();
                        session.CurrentStep = RegistrationSession.Step.AskIngameId;
                        await message.Channel.SendMessageAsync("What is your **in-game ID**?\nExample: `1602584`");
                        return;

                    case RegistrationSession.Step.AskIngameId:

                        var ingameIdInput = message.Content.Trim();

                        if (!long.TryParse(ingameIdInput, out _))
                        {
                            await message.Channel.SendMessageAsync("Your **Ingame ID** must contain **numbers only**.\nExample: `1602584`\nPlease try again:");
                            return;
                        }

                        session.IngameId = ingameIdInput;
                        session.CurrentStep = RegistrationSession.Step.AskMight;
                        await message.Channel.SendMessageAsync("What is your **Might**?\nExample: `120000000`");
                        return;

                    case RegistrationSession.Step.AskMight:
                        if (!int.TryParse(message.Content.Trim(), out var might))
                        {
                            await message.Channel.SendMessageAsync("Please enter a **valid number** for Might.\nExample: `120000000`");
                            return;
                        }

                        session.Might = might;
                        session.CurrentStep = RegistrationSession.Step.AskKillPoints;
                        await message.Channel.SendMessageAsync("What are your **Kill Points**?\nExample: `1000000`");
                        return;

                    case RegistrationSession.Step.AskKillPoints:
                        if (!long.TryParse(message.Content.Trim(), out var kills))
                        {
                            await message.Channel.SendMessageAsync("Please enter a **valid number** for Kill Points.\nExample: `1000000`");
                            return;
                        }

                        session.KillPoints = kills;
                        session.CurrentStep = RegistrationSession.Step.AskCollectorLevel;
                        await message.Channel.SendMessageAsync("What is your **Collector Level**?\nExample: `25`");
                        return;

                    case RegistrationSession.Step.AskCollectorLevel:
                        if (!int.TryParse(message.Content.Trim(), out var collector))
                        {
                            await message.Channel.SendMessageAsync("Please enter a **valid number** for Collector Level.\nExample: `25`");
                            return;
                        }

                        session.CollectorLevel = collector;
                        session.CurrentStep = RegistrationSession.Step.Complete;

                        // Save to Google Sheets
                        var newMember = new Member
                        {
                            DiscordUserId = userId.ToString(),
                            IngameName = session.IngameName,
                            IngameId = session.IngameId,
                            Might = session.Might,
                            KillPoints = session.KillPoints,
                            CollectorLevel = session.CollectorLevel,
                            ReignPoints = 0,
                            IsExempt = false,
                            LastUpdatedUTC = DateTime.UtcNow
                        };

                        await memberService.RegisterOrUpdateAsync(newMember);

                        await message.Channel.SendMessageAsync("🎉 **Registration complete!**\nYou are now registered in the tribe database!, If you wish to update anything type !register");

                        _registrationSessions.Remove(userId);
                        return;
                }
            }

            // ============================
            // OTHER COMMANDS
            // ============================

            if (message.Content == "!ping")
            {
                await message.Channel.SendMessageAsync("Pong!");
                return;
            }

            if (message.Content == "!members")
            {
                var memberService = _services.GetService<IMemberService>();
                var members = await memberService.GetAllMembersAsync();

                if (members.Count == 0)
                {
                    await message.Channel.SendMessageAsync("No members found in sheet.");
                }
                else
                {
                    await message.Channel.SendMessageAsync($"Members found: {members.Count}");
                }

                return;
            }
        }
    }
}
