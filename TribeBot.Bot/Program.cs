using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
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
        private DateTime _lastReminderDate = DateTime.MinValue;
        private bool _reignLocked = false;

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
            _client.InteractionCreated += HandleInteractionAsync;

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

        private async Task ReadyAsync()
        {
            Console.WriteLine($"Connected as {_client.CurrentUser}");

            // ======================================================
            // REGISTER GLOBAL SLASH COMMAND (REQUIRED FOR BADGE)
            // ======================================================
            try
            {
                var globalCommand = new SlashCommandBuilder()
                    .WithName("checkbot")
                    .WithDescription("Checks if the bot is online globally.");

                await _client.CreateGlobalApplicationCommandAsync(globalCommand.Build());
                Console.WriteLine("GLOBAL slash command '/checkbot' registered.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error registering GLOBAL slash command: " + ex.Message);
            }

            // ======================================================
            // REGISTER GUILD SLASH COMMAND (INSTANT UPDATE)
            // ======================================================
            var guild = _client.GetGuild(1109193500664287336); // your guild ID

            var guildCommand = new SlashCommandBuilder()
                .WithName("checkbot")
                .WithDescription("Checks if the bot is online.");

            try
            {
                await guild.CreateApplicationCommandAsync(guildCommand.Build());
                Console.WriteLine("GUILD slash command '/checkbot' registered.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error registering GUILD slash command: " + ex.Message);
            }

            // Reminders
            StartRegistrationReminderLoop();
        }


        private void StartRegistrationReminderLoop()
        {
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        var now = DateTime.UtcNow;

                        // Trigger reminder at 20:00 UTC, once per day
                        if (now.Hour == 20 && now.Minute == 0 && _lastReminderDate.Date != now.Date)
                        {
                            _lastReminderDate = now.Date;
                            await SendRegistrationRemindersAsync();
                        }

                        // TEST

                        //if (now.Minute % 2 == 0)
                        //{
                        //    await SendRegistrationRemindersAsync();
                        //}
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Reminder error: {ex.Message}");
                    }

                    await Task.Delay(TimeSpan.FromMinutes(1));
                }
            });
        }

        // ======================================================
        // REMINDERS FOR UNREGISTERED USERS
        // ======================================================

        private async Task SendRegistrationRemindersAsync()
        {
            try
            {
                var guild = _client.GetGuild(1109193500664287336); // ID of the server

                if (guild == null)
                {
                    Console.WriteLine("Server not found");
                    return;
                }

                //var hogsRole = guild.GetRole(1222668156271591485) // Members Hogs role code CHANGE WHEN LIVE
                var hogsRole = guild.GetRole(1439972286877794314); // test role

                if (hogsRole == null)
                {
                    Console.WriteLine("Hogs role not found");
                    return;
                }

                // Get list of the registered members from the sheets (THESE DO NOT NEED TO BE REMINDED TO REGISTER)

                var memberService = _services.GetService<IMemberService>();
                var registered = await memberService.GetAllMembersAsync();
                var registeredIds = new HashSet<string>(registered.Select(m => m.DiscordUserId));

                //Filter for unregistered hogs users 
                var unregistered = new List<SocketGuildUser>();

                foreach (var user in hogsRole.Members)
                {
                    if (!registeredIds.Contains(user.Id.ToString()))
                    {
                        unregistered.Add(user); // new list created only with unregistered users with HOGS role
                    }
                }

                if (unregistered.Count == 0)
                {
                    Console.WriteLine("All hogs have been registered, Checking back tomorrow");
                    return;
                }

                Console.WriteLine($"Sending DM reminders to {unregistered.Count} users...");

                //Send dm reminders
                foreach (var user in unregistered)
                {
                    try
                    {
                        var dm = await user.CreateDMChannelAsync();

                        await dm.SendMessageAsync(
                            $"Hello {user.Username}! 👋\n\n" +
                            "**You still need to register with the Tribe Bot.**\n\n" +
                            "Please send `!register` here in DM to begin your registration.\n\n" +
                            "Thank you! 😊");
                    }
                    catch
                    {
                        Console.WriteLine($"Could not DM {user.Username}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Reminder job failed: {ex.Message}");
            }
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

            // ============================
            // CONTINUE REGISTRATION FLOW
            // ============================

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

                        await message.Channel.SendMessageAsync("🎉 **Registration complete!**\nYou are now registered in the tribe database! If you wish to update anything type `!register` again.");

                        _registrationSessions.Remove(userId);
                        return;
                }
            }

            // ============================
            // APPLY FOR REIGN EVENT
            // ============================

            if (message.Content.Equals("!applyreign", StringComparison.OrdinalIgnoreCase))
            {


                if (_reignLocked)
                {
                    await message.Channel.SendMessageAsync("⛔ You lack the key to success (Reign list is currently locked message an officer for more information)");
                    return;
                }

                var reignService = _services.GetService<IReignService>();
                var memberService = _services.GetService<IMemberService>();

                // Must be registered
                var member = await memberService.GetMemberByDiscordIdAsync(message.Author.Id.ToString());

                if (member == null)
                {
                    await message.Channel.SendMessageAsync($"{message.Author.Mention} You must register first, Please type `!register`.");
                    return;
                }

                //Apply
                try
                {
                    await reignService.ApplyAsync(message.Author.Id.ToString());
                    await message.Channel.SendMessageAsync($"{message.Author.Mention} You've been added in the viking reign list, type !listreign to view your ranking");
                }
                catch (Exception ex)
                {
                    await message.Channel.SendMessageAsync($"{message.Author.Mention} {ex.Message}");
                    Console.WriteLine(ex.ToString());
                }
                return;
            }

            // ============================
            // LIST ALL REIGN APPLICANTS
            // ============================

            if (message.Content.Equals("!listreign", StringComparison.OrdinalIgnoreCase))
            {
                var reignService = _services.GetService<IReignService>();
                var results = await reignService.GetCurrentRegistrationsSortedAsync();

                if (results.Count == 0)
                {
                    await message.Channel.SendMessageAsync("Nobody has applied for the reign event yet.");
                    return;
                }

                string output = "🏆 **Reign Applicants (sorted by Reign Points(ASC))**:\n\n";

                int position = 1;

                foreach (var (member, reg) in results)
                {
                    output += $"{position}) **{member.IngameName}** - {member.ReignPoints} pts\n";
                    position++;
                }

                await message.Channel.SendMessageAsync(output);
                return;
            }

            // ============================
            // CLEAR REIGN REGISTRATION LIST (HOGS R4 OFFICERS ONLY)
            // ============================

            if (message.Content.Equals("!clearreign", StringComparison.OrdinalIgnoreCase))
            {
                if (message.Channel is SocketGuildChannel guildChannel)
                {
                    var guildUser = message.Author as SocketGuildUser;


                    //Officer role check 
                    bool isOfficer = guildUser.Roles.Any(r => r.Id == 1222665812775534592);

                    if (!isOfficer)
                    {
                        await message.Channel.SendMessageAsync($"{message.Author.Mention} Oi you cheeky cunt, tryna clear the reign list? nice try, peasants can't use this sorry not sorry.");
                        return;
                    }

                    var reignService = _services.GetService<IReignService>();
                    await reignService.ClearAsync();

                    await message.Channel.SendMessageAsync("🧹 **Reign application list has been cleared by an officer!**");
                    return;
                }
            }

            // ============================
            // LOCK REIGN APPLICATIONS (HOGS R4 OFFICERS ONLY)
            // ============================

            if (message.Content.Equals("!lockreign", StringComparison.OrdinalIgnoreCase))
            {
                if(message.Channel is SocketGuildChannel)
                {
                    var guildUser = message.Author as SocketGuildUser;

                    //Officer role check 
                    bool isOfficer = guildUser.Roles.Any(r => r.Id == 1222665812775534592);

                    if (!isOfficer)
                    {
                        await message.Channel.SendMessageAsync($"{message.Author.Mention} you've got the wrong key to lock this door chap, stop being a retard. (Officer only command)");
                        return;
                    }

                    _reignLocked = true;

                    await message.Channel.SendMessageAsync("🔒 **Reign applications are now LOCKED.**");
                    return;
                }
            }

            // ============================
            // UNLOCK REIGN APPLICATIONS (HOGS R4 OFFICERS ONLY)
            // ============================

            if (message.Content.Equals("!unlockreign", StringComparison.OrdinalIgnoreCase))
            {
                if (message.Channel is SocketGuildChannel)
                {
                    var guildUser = message.Author as SocketGuildUser;

                    //Officer role check 
                    bool isOfficer = guildUser.Roles.Any(r => r.Id == 1222665812775534592);

                    if (!isOfficer)
                    {
                        await message.Channel.SendMessageAsync($"{message.Author.Mention} You shall not unlock this list, You have no power here...., List remains locked (Officer only command)");
                        return;
                    }

                    _reignLocked = false;

                    await message.Channel.SendMessageAsync("🔓 **Reign applications are now UNLOCKED.**");
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
        // ============================
        // SLASH COMMAND HANDLER
        // ============================

        private async Task HandleInteractionAsync(SocketInteraction interaction)
        {
            try
            {
                if (interaction is SocketSlashCommand slashCommand)
                {
                    switch (slashCommand.Data.Name)
                    {
                        case "checkbot":
                            await slashCommand.RespondAsync("Bot is online and running! ⚡");
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Interaction error: " + ex.Message);
            }
        }
    }
}
