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

        // Registration sessions and Update sessions stored per-user
        private Dictionary<ulong, RegistrationSession> _registrationSessions = new();
        private Dictionary<ulong, RegistrationSession> _updateAllSessions = new();

        //Pay-for override session (Per discord user) 
        private Dictionary<ulong, string> _payForSessions = new();

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
            string paddlePath = @"C:\PaddleOCR\PaddleOCR-json_v1.4.1\PaddleOCR-json.exe";

            // Google Sheets datastore
            services.AddSingleton<IGoogleSheetsDataStore>(provider =>
                new GoogleSheetsDataStore(credentialsPath, spreadsheetId));

            // OCR service
            services.AddSingleton(new PaddleOcrServerService(paddlePath));

            // TribeBot services
            services.AddSingleton<IMemberService, MemberService>();
            services.AddSingleton<IReignService, ReignService>();
            services.AddSingleton<IDonationService, DonationService>();
            services.AddSingleton<IFineService, FineService>();

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

            // GLOBAL slash command (badge requirement)
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

            // GUILD slash command (instant update)
            var guild = _client.GetGuild(1109193500664287336); // HOGS guild

            try
            {
                var guildCommand = new SlashCommandBuilder()
                    .WithName("checkbot")
                    .WithDescription("Checks if the bot is online.");

                await guild.CreateApplicationCommandAsync(guildCommand.Build());
                Console.WriteLine("GUILD slash command '/checkbot' registered.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error registering GUILD slash command: " + ex.Message);
            }

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

                        if (now.Hour == 20 && now.Minute == 0 &&
                            _lastReminderDate.Date != now.Date)
                        {
                            _lastReminderDate = now.Date;
                            await SendRegistrationRemindersAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Reminder error: {ex.Message}");
                    }

                    await Task.Delay(TimeSpan.FromMinutes(1));
                }
            });
        }

        private async Task SendRegistrationRemindersAsync()
        {
            try
            {
                var guild = _client.GetGuild(1109193500664287336);

                // TEST ROLE → replace with live HOGS when ready
                var hogsRole = guild.GetRole(1439972286877794314);

                if (hogsRole == null)
                {
                    Console.WriteLine("Hogs role not found");
                    return;
                }

                var memberService = _services.GetService<IMemberService>();
                var registered = await memberService.GetAllMembersAsync();
                var registeredIds = registered.Select(m => m.DiscordUserId).ToHashSet();

                var unregistered = hogsRole.Members
                    .Where(u => !registeredIds.Contains(u.Id.ToString()))
                    .ToList();

                if (unregistered.Count == 0)
                {
                    Console.WriteLine("All hogs have been registered.");
                    return;
                }

                Console.WriteLine($"Sending DM reminders to {unregistered.Count} users...");

                foreach (var user in unregistered)
                {
                    try
                    {
                        var dm = await user.CreateDMChannelAsync();
                        await dm.SendMessageAsync(
                            $"Hello {user.Username}! 👋\n\n" +
                            "**You still need to register with the Tribe Bot.**\n\n" +
                            "Please send `!register` here in DM to begin.");
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

            #region REGISTRATION START

            if (message.Content.Equals("!register", StringComparison.OrdinalIgnoreCase))
            {
                var guild = _client.GetGuild(1109193500664287336);
                var guildUser = guild?.GetUser(userId);

                if (guildUser == null)
                {
                    await message.Channel.SendMessageAsync("You must be in the server to register.");
                    return;
                }

                bool hasRole = guildUser.Roles.Any(r => r.Id == 1222668156271591485);
                if (!hasRole)
                {
                    await message.Channel.SendMessageAsync(
                        "You do not have the **Member HOGS** role.");
                    return;
                }

                try
                {
                    var dm = await message.Author.CreateDMChannelAsync();
                    await dm.SendMessageAsync(
                        "Let's get you registered! 😊\n\n" +
                        "What is your **in-game name**?\nExample: `OneClick`");
                }
                catch
                {
                    await message.Channel.SendMessageAsync(
                        $"{message.Author.Mention} I couldn't DM you.");
                    return;
                }

                _registrationSessions[userId] = new RegistrationSession
                {
                    CurrentStep = RegistrationSession.Step.AskIngameName
                };

                return;
            }

            #endregion


            #region REGISTRATION OR UPDATE CONTINUATION (DM ONLY)

            // ============================
            // UPDATE ALL FLOW (DM ONLY)
            // ============================

            if (isDM && _updateAllSessions.ContainsKey(userId))
            {
                var session = _updateAllSessions[userId];
                var memberService = _services.GetService<IMemberService>();

                string input = message.Content.Trim();
                var member = await memberService.GetMemberByDiscordIdAsync(userId.ToString());

                switch (session.CurrentStep)
                {
                    // NAME
                    case RegistrationSession.Step.AskIngameName:
                        if (!input.Equals("same", StringComparison.OrdinalIgnoreCase))
                        {
                            if (string.IsNullOrWhiteSpace(input) ||
                                input.Length > 20 ||
                                !System.Text.RegularExpressions.Regex.IsMatch(input, @"^[A-Za-z0-9 ]+$"))
                            {
                                await message.Channel.SendMessageAsync("❌ Invalid name. Try again:");
                                return;
                            }

                            session.IngameName = input;
                        }

                        session.CurrentStep = RegistrationSession.Step.AskIngameId;
                        await message.Channel.SendMessageAsync(
                            $"**Current ID:** {session.IngameId}\nEnter new ID or type `same`:"
                        );
                        return;

                    // INGAME ID
                    case RegistrationSession.Step.AskIngameId:
                        if (!input.Equals("same", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!long.TryParse(input, out long id) || id < 1 || id > 9999999999)
                            {
                                await message.Channel.SendMessageAsync("❌ Invalid ID. Try again:");
                                return;
                            }

                            // duplicate check — allow own ID
                            var allMembers = await memberService.GetAllMembersAsync();
                            if (allMembers.Any(m => m.IngameId == input && m.DiscordUserId != userId.ToString()))
                            {
                                await message.Channel.SendMessageAsync("❌ That ID belongs to another member. Try again:");
                                return;
                            }

                            session.IngameId = input;
                        }

                        session.CurrentStep = RegistrationSession.Step.AskMight;
                        await message.Channel.SendMessageAsync(
                            $"**Current Might:** {session.Might:N0}\nEnter new Might or type `same`:"
                        );
                        return;

                    // MIGHT
                    case RegistrationSession.Step.AskMight:
                        if (!input.Equals("same", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!long.TryParse(input, out long might) || might < 0 || might > 3000000000)
                            {
                                await message.Channel.SendMessageAsync("❌ Invalid Might (0–3B). Try again:");
                                return;
                            }

                            session.Might = (int)might;
                        }

                        session.CurrentStep = RegistrationSession.Step.AskKillPoints;
                        await message.Channel.SendMessageAsync(
                            $"**Current Kill Points:** {session.KillPoints:N0}\nEnter new Kill Points or type `same`:"
                        );
                        return;

                    // KILL POINTS
                    case RegistrationSession.Step.AskKillPoints:
                        if (!input.Equals("same", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!long.TryParse(input, out long kills) || kills < 0 || kills > 500000000000)
                            {
                                await message.Channel.SendMessageAsync("❌ Invalid Kill Points. Try again:");
                                return;
                            }

                            session.KillPoints = kills;
                        }

                        session.CurrentStep = RegistrationSession.Step.AskCollectorLevel;
                        await message.Channel.SendMessageAsync(
                            $"**Current Collector Level:** {session.CollectorLevel}\nEnter new Level (0–100) or type `same`:"
                        );
                        return;

                    // COLLECTOR LEVEL — SAVE UPDATE
                    case RegistrationSession.Step.AskCollectorLevel:
                        if (!input.Equals("same", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!int.TryParse(input, out int col) || col < 0 || col > 100)
                            {
                                await message.Channel.SendMessageAsync("❌ Invalid Collector Level. Try again:");
                                return;
                            }

                            session.CollectorLevel = col;
                        }

                        // SAVE UPDATED PROFILE
                        member.IngameName = session.IngameName;
                        member.IngameId = session.IngameId;
                        member.Might = session.Might;
                        member.KillPoints = session.KillPoints;
                        member.CollectorLevel = session.CollectorLevel;
                        member.LastUpdatedUTC = DateTime.UtcNow;

                        await memberService.RegisterOrUpdateAsync(member);

                        await message.Channel.SendMessageAsync("✅ Profile updated successfully!");

                        // LOG TO OFFICER CHANNEL
                        ulong officerLogChannelId = 1440209811621937273;
                        var logChannel = _client.GetChannel(officerLogChannelId) as IMessageChannel;
                        if (logChannel != null)
                        {
                            await logChannel.SendMessageAsync(
                                $"🔄 **Profile Updated**\n" +
                                $"• <@{userId}>\n" +
                                $"• Name: {member.IngameName}\n" +
                                $"• ID: {member.IngameId}\n" +
                                $"• Might: {member.Might:N0}\n" +
                                $"• Kills: {member.KillPoints:N0}\n" +
                                $"• Collector: {member.CollectorLevel}\n" +
                                $"• Time: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC"
                            );
                        }

                        _updateAllSessions.Remove(userId);
                        return;
                }
            }

            // ============================
            // REGISTRATION FLOW (DM ONLY)
            // ============================

            if (isDM && _registrationSessions.ContainsKey(userId))
            {
                var session = _registrationSessions[userId];
                var memberService = _services.GetService<IMemberService>();

                switch (session.CurrentStep)
                {
                    // ——— STEP 1: IngameName ———
                    case RegistrationSession.Step.AskIngameName:
                        {
                            string name = message.Content.Trim();

                            if (string.IsNullOrWhiteSpace(name))
                            {
                                await message.Channel.SendMessageAsync(
                                    "❌ Name cannot be empty.");
                                return;
                            }

                            if (name.Length > 20)
                            {
                                await message.Channel.SendMessageAsync(
                                    "❌ Max length is **20 characters**.");
                                return;
                            }

                            if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[A-Za-z0-9 ]+$"))
                            {
                                await message.Channel.SendMessageAsync(
                                    "❌ Only letters, numbers and spaces allowed.");
                                return;
                            }

                            session.IngameName = name;
                            session.CurrentStep = RegistrationSession.Step.AskIngameId;
                            await message.Channel.SendMessageAsync(
                                "Enter your **In-game ID** (1–10 digits).");
                            return;
                        }

                    // ——— STEP 2: IngameID w/ duplicate check ———
                    case RegistrationSession.Step.AskIngameId:
                        {
                            string input = message.Content.Trim();

                            if (!long.TryParse(input, out long idVal) ||
                                idVal < 1 || idVal > 9999999999)
                            {
                                await message.Channel.SendMessageAsync(
                                    "❌ Invalid ID. Must be 1–10 digits.");
                                return;
                            }

                            var allMembers = await memberService.GetAllMembersAsync();
                            if (allMembers.Any(m => m.IngameId == input))
                            {
                                await message.Channel.SendMessageAsync(
                                    "❌ This ID is already registered.");
                                return;
                            }

                            session.IngameId = input;
                            session.CurrentStep = RegistrationSession.Step.AskMight;
                            await message.Channel.SendMessageAsync(
                                "Enter your **Might** (0–3000000000).");
                            return;
                        }

                    // ——— STEP 3: Might ———
                    case RegistrationSession.Step.AskMight:
                        {
                            if (!long.TryParse(message.Content.Trim(), out long might) ||
                                might < 0 || might > 3000000000)
                            {
                                await message.Channel.SendMessageAsync(
                                    "❌ Invalid Might. 0–3000000000 allowed.");
                                return;
                            }

                            session.Might = (int)might;
                            session.CurrentStep = RegistrationSession.Step.AskKillPoints;
                            await message.Channel.SendMessageAsync(
                                "Enter your **Kill Points** (0–500000000000).");
                            return;
                        }

                    // ——— STEP 4: Kill Points ———
                    case RegistrationSession.Step.AskKillPoints:
                        {
                            if (!long.TryParse(message.Content.Trim(), out long kills) ||
                                kills < 0 || kills > 500000000000)
                            {
                                await message.Channel.SendMessageAsync(
                                    "❌ Invalid Kill Points. 0–500000000000 allowed.");
                                return;
                            }

                            session.KillPoints = kills;
                            session.CurrentStep = RegistrationSession.Step.AskCollectorLevel;
                            await message.Channel.SendMessageAsync(
                                "Enter your **Collector Level** (0–100).");
                            return;
                        }

                    // ——— STEP 5: Collector Level → SAVE ———
                    case RegistrationSession.Step.AskCollectorLevel:
                        {
                            if (!int.TryParse(message.Content.Trim(), out int collector) ||
                                collector < 0 || collector > 100)
                            {
                                await message.Channel.SendMessageAsync(
                                    "❌ Invalid Collector Level.");
                                return;
                            }

                            session.CollectorLevel = collector;

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

                            await message.Channel.SendMessageAsync(
                                "🎉 **Registration complete!**");

                            // Officer log
                            ulong officerLogChannelId = 1440209811621937273;
                            var logChannel = _client.GetChannel(officerLogChannelId) as IMessageChannel;

                            if (logChannel != null)
                            {
                                await logChannel.SendMessageAsync(
                                    $"📝 **New Registration**\n" +
                                    $"• Discord: <@{userId}>\n" +
                                    $"• Name: **{session.IngameName}**\n" +
                                    $"• ID: `{session.IngameId}`\n" +
                                    $"• Might: `{session.Might:N0}`\n" +
                                    $"• Kills: `{session.KillPoints:N0}`\n" +
                                    $"• Collector: `{session.CollectorLevel}`\n" +
                                    $"• Time: `{DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC`");
                            }

                            _registrationSessions.Remove(userId);
                            return;
                        }
                }
            }

            #endregion

            #region MEMBER SELF-UPDATE

            //Step 1 update ingame name 

            if (message.Content.StartsWith("!updateigname ", StringComparison.OrdinalIgnoreCase))
            {
                var memberService = _services.GetService<IMemberService>();
                var member = await memberService.GetMemberByDiscordIdAsync(message.Author.Id.ToString());

                if (member == null)
                {
                    await message.Channel.SendMessageAsync($"{message.Author.Mention} you must register first.");
                    return;
                }

                string newName = message.Content.Substring("!updateigname ".Length).Trim();

                if (string.IsNullOrWhiteSpace(newName) || newName.Length > 20 ||
                    !System.Text.RegularExpressions.Regex.IsMatch(newName, @"^[A-Za-z0-9 ]+$"))
                {
                    await message.Channel.SendMessageAsync("❌ Invalid name. Must be alphanumeric and <= 20 chars.");
                    return;
                }

                member.IngameName = newName;
                member.LastUpdatedUTC = DateTime.UtcNow;

                await memberService.RegisterOrUpdateAsync(member);
                await message.Channel.SendMessageAsync($"✔ Updated your in-game name to **{newName}**.");
                return;
            }

            // step 2 update ingameId

            if (message.Content.StartsWith("!updateid ", StringComparison.OrdinalIgnoreCase))
            {
                var memberService = _services.GetService<IMemberService>();
                var member = await memberService.GetMemberByDiscordIdAsync(message.Author.Id.ToString());

                if (member == null)
                {
                    await message.Channel.SendMessageAsync($"{message.Author.Mention} you must register first.");
                    return;
                }

                string input = message.Content.Substring("!updateid ".Length).Trim();

                if (!long.TryParse(input, out long idVal) || idVal < 1 || idVal > 9999999999)
                {
                    await message.Channel.SendMessageAsync("❌ Invalid ID. Must be 1–10 digits.");
                    return;
                }

                // Duplicate check, but allow CURRENT user's ID
                var allMembers = await memberService.GetAllMembersAsync();
                if (allMembers.Any(m => m.IngameId == input && m.DiscordUserId != message.Author.Id.ToString()))
                {
                    await message.Channel.SendMessageAsync("❌ That ID belongs to another member.");
                    return;
                }

                member.IngameId = input;
                member.LastUpdatedUTC = DateTime.UtcNow;

                await memberService.RegisterOrUpdateAsync(member);
                await message.Channel.SendMessageAsync($"✔ Updated your In-game ID to `{input}`.");
                return;
            }

            //step 3 update might

            if (message.Content.StartsWith("!updatemight ", StringComparison.OrdinalIgnoreCase))
            {
                var memberService = _services.GetService<IMemberService>();
                var member = await memberService.GetMemberByDiscordIdAsync(message.Author.Id.ToString());

                if (member == null)
                {
                    await message.Channel.SendMessageAsync("You must register first.");
                    return;
                }

                string input = message.Content.Substring("!updatemight ".Length).Trim();
                if (!long.TryParse(input, out long might) || might < 0 || might > 3000000000)
                {
                    await message.Channel.SendMessageAsync("❌ Invalid Might. Must be 0–3000000000.");
                    return;
                }

                member.Might = (int)might;
                member.LastUpdatedUTC = DateTime.UtcNow;

                await memberService.RegisterOrUpdateAsync(member);
                await message.Channel.SendMessageAsync($"✔ Might updated to **{might:N0}**.");
                return;
            }

            //step 4 update killpoints 

            if (message.Content.StartsWith("!updatekills ", StringComparison.OrdinalIgnoreCase))
            {
                var memberService = _services.GetService<IMemberService>();
                var member = await memberService.GetMemberByDiscordIdAsync(message.Author.Id.ToString());

                if (member == null)
                {
                    await message.Channel.SendMessageAsync("You must register first.");
                    return;
                }

                string input = message.Content.Substring("!updatekills ".Length).Trim();

                if (!long.TryParse(input, out long kills) || kills < 0 || kills > 500000000000)
                {
                    await message.Channel.SendMessageAsync("❌ Invalid Kill Points. Must be 0–500,000,000,000.");
                    return;
                }

                member.KillPoints = kills;
                member.LastUpdatedUTC = DateTime.UtcNow;

                await memberService.RegisterOrUpdateAsync(member);
                await message.Channel.SendMessageAsync($"✔ Kill Points updated to **{kills:N0}**.");
                return;
            }

            //step 5 update collector level

            if (message.Content.StartsWith("!updatecollector ", StringComparison.OrdinalIgnoreCase))
            {
                var memberService = _services.GetService<IMemberService>();
                var member = await memberService.GetMemberByDiscordIdAsync(message.Author.Id.ToString());

                if (member == null)
                {
                    await message.Channel.SendMessageAsync("You must register first.");
                    return;
                }

                string input = message.Content.Substring("!updatecollector ".Length).Trim();
                if (!int.TryParse(input, out int lvl) || lvl < 0 || lvl > 100)
                {
                    await message.Channel.SendMessageAsync("❌ Invalid Collector Level. Must be 0–100.");
                    return;
                }

                member.CollectorLevel = lvl;
                member.LastUpdatedUTC = DateTime.UtcNow;

                await memberService.RegisterOrUpdateAsync(member);
                await message.Channel.SendMessageAsync($"✔ Collector Level updated to **{lvl}**.");
                return;
            }

            // ============================
            // Update entire profile via DM
            // ============================

            if (message.Content.Equals("!updateall", StringComparison.OrdinalIgnoreCase))
            {
                var memberService = _services.GetService<IMemberService>();
                var member = await memberService.GetMemberByDiscordIdAsync(message.Author.Id.ToString());

                if (member == null)
                {
                    await message.Channel.SendMessageAsync($"{message.Author.Mention} you are not registered. Use `!register` first.");
                    return;
                }

                // Begin DM flow
                try
                {
                    var dm = await message.Author.CreateDMChannelAsync();
                    await dm.SendMessageAsync(
                        "🔄 **Update All Profile Information**\n" +
                        "You can now update your entire profile.\n\n" +
                        "For each field, enter a new value **or type `same` to keep your current value**.\n\n" +
                        $"**Current In-game Name:** {member.IngameName}\n" +
                        "Please enter your new **In-game Name**:"
                    );
                }
                catch
                {
                    await message.Channel.SendMessageAsync($"{message.Author.Mention} I couldn't DM you. Please enable DMs first.");
                    return;
                }

                _updateAllSessions[message.Author.Id] = new RegistrationSession
                {
                    CurrentStep = RegistrationSession.Step.AskIngameName,
                    IngameName = member.IngameName,
                    IngameId = member.IngameId,
                    Might = member.Might,
                    KillPoints = member.KillPoints,
                    CollectorLevel = member.CollectorLevel
                };

                return;
            }

            #endregion

            #region MEMBER ADMINISTRATION

            // ============================
            // !removemember @User (OFFICERS ONLY)
            // ============================

            if (message.Content.StartsWith("!removemember", StringComparison.OrdinalIgnoreCase))
            {
                if (message.Channel is not SocketGuildChannel guildChannel)
                {
                    await message.Channel.SendMessageAsync("This command can only be used inside the server.");
                    return;
                }

                var caller = message.Author as SocketGuildUser;
                ulong officerRoleId = 1222665812775534592;

                if (!caller.Roles.Any(r => r.Id == officerRoleId))
                {
                    await message.Channel.SendMessageAsync($"{caller.Mention} you do not have permission to use this command.");
                    return;
                }

                if (message.MentionedUsers.Count == 0)
                {
                    await message.Channel.SendMessageAsync("Usage: `!removemember @user`");
                    return;
                }

                var target = message.MentionedUsers.First();
                string discordId = target.Id.ToString();

                var dataStore = _services.GetService<IGoogleSheetsDataStore>();
                bool success = await dataStore.RemoveMemberByDiscordIdAsync(discordId);

                if (success)
                    await message.Channel.SendMessageAsync($"✅ Removed **{target.Username}** from the member list.");
                else
                    await message.Channel.SendMessageAsync($"❌ Could not find **{target.Username}** in the member list.");

                return;
            }

            // ============================
            // !listmembers (Alphabetical)
            // ============================

            if (message.Content.Equals("!listmembers", StringComparison.OrdinalIgnoreCase))
            {
                var memberService = _services.GetService<IMemberService>();
                var members = await memberService.GetAllMembersAsync();

                if (members.Count == 0)
                {
                    await message.Channel.SendMessageAsync("No members found.");
                    return;
                }

                var sorted = members.OrderBy(m => m.IngameName, StringComparer.OrdinalIgnoreCase).ToList();

                string msg = "📜 **Member List (Alphabetical)**\n\n";

                foreach (var m in sorted)
                    msg += $"• **{m.IngameName}**  — `{m.IngameId}`\n";

                await SendLongMessageAsync(message.Channel, msg);
                return;
            }

            // ============================
            // !registerreminder (manual)
            // ============================

            if (message.Content.Equals("!registerreminder", StringComparison.OrdinalIgnoreCase))
            {
                if (message.Channel is not SocketGuildChannel regChan)
                {
                    await message.Channel.SendMessageAsync("This command can only be used inside the server.");
                    return;
                }

                var caller = message.Author as SocketGuildUser;
                ulong officerRoleId = 1222665812775534592;

                if (!caller.Roles.Any(r => r.Id == officerRoleId))
                {
                    await message.Channel.SendMessageAsync($"{caller.Mention} you do not have permission to use this command.");
                    return;
                }

                var guild = regChan.Guild;
                var memberService = _services.GetService<IMemberService>();

                var registered = await memberService.GetAllMembersAsync();
                var registeredIds = registered.Select(m => m.DiscordUserId).ToHashSet();

                ulong hogsRoleId = 1222668156271591485; // LIVE ROLE
                var hogsRole = guild.GetRole(hogsRoleId);

                var nonRegistered = hogsRole.Members
                    .Where(u => !registeredIds.Contains(u.Id.ToString()))
                    .ToList();

                if (nonRegistered.Count == 0)
                {
                    await message.Channel.SendMessageAsync("🎉 All HOGS members are registered!");
                    return;
                }

                int sent = 0;
                List<string> failed = new();

                foreach (var user in nonRegistered)
                {
                    try
                    {
                        var dm = await user.CreateDMChannelAsync();
                        await dm.SendMessageAsync(
                            $"👋 Hello **{user.Username}**, you still need to register with the Tribe Bot.\n" +
                            $"Please type `!register` here in DM.\n\n" +
                            $"Registration is required to participate in tribe events.");
                        sent++;
                    }
                    catch
                    {
                        failed.Add($"<@{user.Id}>");
                    }
                }

                string result =
                    $"📨 Registration reminders sent: **{sent}**";

                if (failed.Count > 0)
                {
                    result += "\n⚠ Could not DM:\n";
                    foreach (var f in failed)
                        result += f + "\n";
                }

                await message.Channel.SendMessageAsync(result);
                return;
            }

            // ============================
            // !listnonregistered
            // ============================

            if (message.Content.Equals("!listnonregistered", StringComparison.OrdinalIgnoreCase))
            {
                if (message.Channel is not SocketGuildChannel lnrChan)
                {
                    await message.Channel.SendMessageAsync("This command can only be used in the server.");
                    return;
                }

                var guild = lnrChan.Guild;
                var memberService = _services.GetService<IMemberService>();

                var registered = await memberService.GetAllMembersAsync();
                var registeredIds = registered.Select(m => m.DiscordUserId).ToHashSet();

                ulong hogsRoleId = 1222668156271591485;
                var hogsRole = guild.GetRole(hogsRoleId);

                var nonRegistered = hogsRole.Members
                    .Where(u => !registeredIds.Contains(u.Id.ToString()))
                    .ToList();

                if (nonRegistered.Count == 0)
                {
                    await message.Channel.SendMessageAsync("🎉 Everyone with HOGS role is registered!");
                    return;
                }

                string msg = "❌ **Non-Registered Members**\n\n";

                foreach (var u in nonRegistered)
                    msg += $"• **{u.Username}**\n";

                await SendLongMessageAsync(message.Channel, msg);
                return;
            }

            #endregion 
            #region REIGN SYSTEM

            // ============================
            // !applyreign
            // ============================

            if (message.Content.Equals("!applyreign", StringComparison.OrdinalIgnoreCase))
            {
                if (_reignLocked)
                {
                    await message.Channel.SendMessageAsync(
                        "⛔ The reign list is currently **LOCKED**.\nMessage an officer for more information.");
                    return;
                }

                var fineService = _services.GetService<IFineService>();
                var fines = await fineService.GetFinesForUserAsync(message.Author.Id.ToString());

                // Count active reign strikes only from unpaid fines
                int activeStrikes = fines
                    .Where(f => f.FineType == "Reign" && !f.IsPaid)
                    .Sum(f => f.ReignStrikes);

                if (activeStrikes > 0)
                {
                    await message.Channel.SendMessageAsync(
                        $"{message.Author.Mention} ❌ You cannot apply for Reign.\n" +
                        $"You have **{activeStrikes} Reign Strike(s)**.\n" +
                        $"You must wait for strike reductions when officers lock the Reign list."
                    );
                    return;
                }

                var reignService = _services.GetService<IReignService>();
                var memberService = _services.GetService<IMemberService>();

                var member = await memberService.GetMemberByDiscordIdAsync(message.Author.Id.ToString());

                if (member == null)
                {
                    await message.Channel.SendMessageAsync(
                        $"{message.Author.Mention} You must **register** before applying! Use `!register`.");
                    return;
                }

                try
                {
                    await reignService.ApplyAsync(message.Author.Id.ToString());
                    await message.Channel.SendMessageAsync(
                        $"{message.Author.Mention} ✅ You have been added to the **Viking Reign** list!\nUse `!listreign` to see your ranking.");
                }
                catch (Exception ex)
                {
                    await message.Channel.SendMessageAsync($"{message.Author.Mention} {ex.Message}");
                }

                return;
            }

            // ============================
            // !listreign
            // ============================

            if (message.Content.Equals("!listreign", StringComparison.OrdinalIgnoreCase))
            {
                var reignService = _services.GetService<IReignService>();
                var results = await reignService.GetCurrentRegistrationsSortedAsync();

                if (results.Count == 0)
                {
                    await message.Channel.SendMessageAsync("Nobody has applied for the Reign event yet.");
                    return;
                }

                string output = "🏆 **Reign Applicants (Sorted ASC by ReignPoints)**\n\n";

                int position = 1;
                foreach (var (member, reg) in results)
                {
                    output += $"{position}) **{member.IngameName}** — {member.ReignPoints} pts\n";
                    position++;
                }

                await SendLongMessageAsync(message.Channel, output);
                return;
            }

            // ============================
            // !clearreign (OFFICERS ONLY)
            // ============================

            if (message.Content.Equals("!clearreign", StringComparison.OrdinalIgnoreCase))
            {
                if (message.Channel is SocketGuildChannel)
                {
                    var user = message.Author as SocketGuildUser;
                    ulong officerRoleId = 1222665812775534592;

                    if (!user.Roles.Any(r => r.Id == officerRoleId))
                    {
                        await message.Channel.SendMessageAsync(
                            $"{message.Author.Mention} You cannot clear the list. Officers only.");
                        return;
                    }

                    var reignService = _services.GetService<IReignService>();
                    await reignService.ClearAsync();

                    await message.Channel.SendMessageAsync(
                        "🧹 **Reign application list has been cleared by an officer!**");
                    return;
                }
            }

            // ============================
            // !lockreign (OFFICERS)
            // ============================

            if (message.Content.Equals("!lockreign", StringComparison.OrdinalIgnoreCase))
            {
                if (message.Channel is SocketGuildChannel)
                {
                    var user = message.Author as SocketGuildUser;
                    ulong officerRoleId = 1222665812775534592;

                    if (!user.Roles.Any(r => r.Id == officerRoleId))
                    {
                        await message.Channel.SendMessageAsync(
                            $"{message.Author.Mention} You do not have permission. Officers only.");
                        return;
                    }

                    _reignLocked = true;

                    var fineService = _services.GetService<IFineService>();
                    await fineService.ReduceReignStrikesAsync();

                    await message.Channel.SendMessageAsync("🔒 **Reign applications are now LOCKED. Strikes reduced by 1 for all users.**");

                }
                return;
            }

            // ============================
            // !unlockreign (OFFICERS)
            // ============================

            if (message.Content.Equals("!unlockreign", StringComparison.OrdinalIgnoreCase))
            {
                if (message.Channel is SocketGuildChannel)
                {
                    var user = message.Author as SocketGuildUser;
                    ulong officerRoleId = 1222665812775534592;

                    if (!user.Roles.Any(r => r.Id == officerRoleId))
                    {
                        await message.Channel.SendMessageAsync(
                            $"{message.Author.Mention} You do not have permission. Officers only.");
                        return;
                    }

                    _reignLocked = false;
                    await message.Channel.SendMessageAsync("🔓 **Reign applications are now UNLOCKED.**");
                }
                return;
            }

            #region BANK SYSTEM

            // ============================
            // !banktaxlist
            // ============================

            if (message.Content.Equals("!banktaxlist", StringComparison.OrdinalIgnoreCase))
            {
                var memberService = _services.GetService<IMemberService>();
                var donationService = _services.GetService<IDonationService>();

                var members = await memberService.GetAllMembersAsync();
                var totals = await donationService.GetTotalsForAllUsersThisWeekAsync();

                int goal = 44_000_000;
                string response = "🏦 **Bank Tax List (This Week)**\n\n";

                foreach (var m in members.OrderBy(m => m.IngameName))
                {
                    totals.TryGetValue(m.DiscordUserId, out int paid);

                    string status =
                        m.IsExempt ? "🟦 EXEMPT" :
                        paid >= goal ? "✅ PAID" : "❌ UNPAID";

                    response += $"**{m.IngameName}** — {paid:N0} / 44,000,000 — {status}\n";
                }

                await SendLongMessageAsync(message.Channel, response);
                return;
            }

            // ============================
            // !bankunpaid
            // ============================

            if (message.Content.Equals("!bankunpaid", StringComparison.OrdinalIgnoreCase))
            {
                var memberService = _services.GetService<IMemberService>();
                var donationService = _services.GetService<IDonationService>();

                var members = await memberService.GetAllMembersAsync();
                var totals = await donationService.GetTotalsForAllUsersThisWeekAsync();

                int goal = 44_000_000;

                var unpaid = members
                    .Where(m => !m.IsExempt &&
                                (!totals.ContainsKey(m.DiscordUserId) ||
                                 totals[m.DiscordUserId] < goal))
                    .OrderBy(m => m.IngameName)
                    .ToList();

                if (unpaid.Count == 0)
                {
                    await message.Channel.SendMessageAsync("🎉 Everyone has paid (or is exempt)!");
                    return;
                }

                string msg = "❌ **Unpaid Members (This Week)**\n\n";
                foreach (var m in unpaid)
                    msg += $"• **{m.IngameName}**\n";

                await SendLongMessageAsync(message.Channel, msg);
                return;
            }

            // ============================
            // !checkbank
            // ============================

            if (message.Content.Equals("!checkbank", StringComparison.OrdinalIgnoreCase))
            {
                var memberService = _services.GetService<IMemberService>();
                var donationService = _services.GetService<IDonationService>();

                string id = message.Author.Id.ToString();
                var member = await memberService.GetMemberByDiscordIdAsync(id);

                if (member == null)
                {
                    await message.Channel.SendMessageAsync(
                        $"{message.Author.Mention} you are not registered. Use `!register`.");
                    return;
                }

                string donationChannel = "<#1440050111353721053>";
                int goal = 44_000_000;

                if (member.IsExempt)
                {
                    await message.Channel.SendMessageAsync(
                        $"{message.Author.Mention} 🟦 **You are exempt from weekly bank donations.**");
                    return;
                }

                int paid = await donationService.GetTotalForUserThisWeekAsync(id);

                if (paid >= goal)
                {
                    await message.Channel.SendMessageAsync(
                        $"{message.Author.Mention} 🎉 **You have paid your bank donation this week!**\n" +
                        $"You donated **{paid:N0} / 44,000,000**.\nThanks for supporting the tribe! 🐗");
                }
                else
                {
                    await message.Channel.SendMessageAsync(
                        $"{message.Author.Mention} ❌ **You have NOT completed your bank donation this week.**\n" +
                        $"You have donated **{paid:N0} / 44,000,000**.\n" +
                        $"Please upload your screenshot in {donationChannel}.");
                }

                return;
            }

            // ============================
            // !bankreminder (OFFICERS ONLY)
            // ============================

            if (message.Content.Equals("!bankreminder", StringComparison.OrdinalIgnoreCase))
            {
                if (message.Channel is not SocketGuildChannel brChan)
                {
                    await message.Channel.SendMessageAsync("This command must be used in the server.");
                    return;
                }

                var caller = message.Author as SocketGuildUser;
                ulong officerRoleId = 1222665812775534592;

                if (!caller.Roles.Any(r => r.Id == officerRoleId))
                {
                    await message.Channel.SendMessageAsync(
                        $"{caller.Mention} you do not have permission. Officers only.");
                    return;
                }

                var guild = brChan.Guild;
                var memberService = _services.GetService<IMemberService>();
                var donationService = _services.GetService<IDonationService>();

                var members = await memberService.GetAllMembersAsync();
                var totals = await donationService.GetTotalsForAllUsersThisWeekAsync();

                int goal = 44_000_000;

                var unpaid = members
                    .Where(m => !m.IsExempt &&
                                (!totals.ContainsKey(m.DiscordUserId) ||
                                 totals[m.DiscordUserId] < goal))
                    .ToList();

                if (unpaid.Count == 0)
                {
                    await message.Channel.SendMessageAsync("🎉 Everyone has paid (or is exempt)!");
                    return;
                }

                int sent = 0;
                List<string> failed = new();

                foreach (var m in unpaid)
                {
                    var user = guild.GetUser(ulong.Parse(m.DiscordUserId));
                    if (user == null)
                    {
                        failed.Add($"<@{m.DiscordUserId}>");
                        continue;
                    }

                    try
                    {
                        var dm = await user.CreateDMChannelAsync();
                        await dm.SendMessageAsync(
                            $"🏦 Hello **{m.IngameName}**, this is your weekly donation reminder.\n" +
                            $"You still need to pay **44,000,000**.\n" +
                            $"Please upload your screenshot in <#1440050111353721053>.");
                        sent++;
                    }
                    catch
                    {
                        failed.Add($"<@{m.DiscordUserId}>");
                    }
                }

                string result = $"📩 Reminder sent to **{sent}** unpaid members.";

                if (failed.Count > 0)
                {
                    result += "\n⚠ Could not DM:\n";
                    foreach (var f in failed) result += f + "\n";
                }

                await message.Channel.SendMessageAsync(result);
                return;
            }

            #endregion

            #region DONATION OCR

            ulong DONATION_CHANNEL_ID = 1440050111353721053;

            if (message.Channel.Id == DONATION_CHANNEL_ID && message.Attachments.Count > 0)
            {
                var donationService = _services.GetService<IDonationService>();
                var ocrService = _services.GetService<PaddleOcrServerService>();
                var memberService = _services.GetService<IMemberService>();

                string uploaderId = message.Author.Id.ToString();
                string targetDiscordId = uploaderId;
                string targetIngameName = null;

                // PAYFOR SESSION CHECK
                if (_payForSessions.TryGetValue(message.Author.Id, out string overrideTarget))
                {
                    if (overrideTarget.StartsWith("DISCORD:"))
                        targetDiscordId = overrideTarget.Substring("DISCORD:".Length);

                    else if (overrideTarget.StartsWith("NAME:"))
                        targetIngameName = overrideTarget.Substring("NAME:".Length);

                    _payForSessions.Remove(message.Author.Id);
                }

                // Resolve the target member
                Member targetMember;

                if (targetIngameName != null)
                {
                    var allMembers = await memberService.GetAllMembersAsync();
                    targetMember = allMembers.FirstOrDefault(m =>
                        m.IngameName.Equals(targetIngameName, StringComparison.OrdinalIgnoreCase));
                }
                else
                {
                    targetMember = await memberService.GetMemberByDiscordIdAsync(targetDiscordId);
                }

                if (targetMember == null)
                {
                    await message.Channel.SendMessageAsync(
                        $"{message.Author.Mention} ❌ Could not find the target account. Donation not recorded.");
                    return;
                }

                // OCR the screenshot(s)
                int totalAmount = 0;

                foreach (var attachment in message.Attachments)
                {
                    if (attachment.ContentType == null || !attachment.ContentType.StartsWith("image"))
                        continue;

                    string tempPath = System.IO.Path.Combine(
                        System.IO.Path.GetTempPath(),
                        Guid.NewGuid().ToString() + ".png");

                    using (var client = new System.Net.Http.HttpClient())
                    {
                        var bytes = await client.GetByteArrayAsync(attachment.Url);
                        await System.IO.File.WriteAllBytesAsync(tempPath, bytes);
                    }

                    int? amount = await ocrService.ExtractDonationAmountAsync(tempPath);
                    System.IO.File.Delete(tempPath);

                    if (amount != null)
                        totalAmount += amount.Value;
                }

                if (totalAmount <= 0)
                {
                    await message.AddReactionAsync(new Emoji("❌"));
                    await message.Channel.SendMessageAsync(
                        $"{message.Author.Mention} I could not read any donation amount.");
                    return;
                }

                // Determine week boundaries
                var now = DateTime.UtcNow;
                int daysSinceMonday = ((int)now.DayOfWeek + 6) % 7;
                DateTime weekStart = now.Date.AddDays(-daysSinceMonday);
                DateTime weekEnd = weekStart.AddDays(7);

                // SAVE DONATION — Corrected to assign to TARGET MEMBER
                await donationService.AddDonationAsync(new DonationRecord
                {
                    DiscordUserId = targetMember.DiscordUserId,
                    IngameName = targetMember.IngameName,
                    Amount = totalAmount,
                    TimestampUtc = now,
                    WeekStartUtc = weekStart,
                    WeekEndUtc = weekEnd
                });

                await message.AddReactionAsync(new Emoji("✅"));
                await message.Channel.SendMessageAsync(
                    $"{message.Author.Mention} donation of **{totalAmount:N0}** recorded for **{targetMember.IngameName}**!");
                return;
            }

            // ============================
            // !payfor <ingamename> OR !payfor @user
            // ============================
            if (message.Content.StartsWith("!payfor", StringComparison.OrdinalIgnoreCase))
            {
                var memberService = _services.GetService<IMemberService>();

                string args = message.Content.Substring("!payfor".Length).Trim();

                if (string.IsNullOrWhiteSpace(args))
                {
                    await message.Channel.SendMessageAsync(
                        "Usage:\n`!payfor <IngameName>`\n`!payfor @DiscordUser`");
                    return;
                }

                // CASE 1 — Paying for @DiscordUser
                if (message.MentionedUsers.Count > 0)
                {
                    var targetUser = message.MentionedUsers.First();
                    string targetDiscordId = targetUser.Id.ToString();

                    var targetMember = await memberService.GetMemberByDiscordIdAsync(targetDiscordId);
                    if (targetMember == null)
                    {
                        await message.Channel.SendMessageAsync(
                            $"❌ <@{targetUser.Id}> is **not registered**.");
                        return;
                    }

                    _payForSessions[message.Author.Id] = $"DISCORD:{targetDiscordId}";

                    await message.Channel.SendMessageAsync(
                        $"💰 Your next donation screenshot(s) will be assigned to **<@{targetUser.Id}>**.");
                    return;
                }

                // CASE 2 — Paying for alt via IngameName
                string ingameName = args;
                var allMembers = await memberService.GetAllMembersAsync();

                var matches = allMembers
                    .Where(m => m.IngameName.Equals(ingameName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (matches.Count == 0)
                {
                    await message.Channel.SendMessageAsync(
                        $"❌ No registered account found with name **{ingameName}**.");
                    return;
                }

                if (matches.Count > 1)
                {
                    await message.Channel.SendMessageAsync(
                        $"⚠ Multiple accounts named **{ingameName}** exist.\nUse: `!payfor @DiscordUser`.");
                    return;
                }

                _payForSessions[message.Author.Id] = $"NAME:{ingameName}";

                await message.Channel.SendMessageAsync(
                    $"💰 Your next donation screenshot(s) will be assigned to **{ingameName}**.");
                return;
            }

            // ============================
            // !fineuser @user amount reason
            // ============================

            if (message.Content.StartsWith("!fineuser", StringComparison.OrdinalIgnoreCase))
            {
                if (message.Channel is not SocketGuildChannel guildchannel)
                {
                    await message.Channel.SendMessageAsync("This command must be used in the server.");
                    return;
                }

                var caller = message.Author as SocketGuildUser;
                ulong officerRoleId = 1222665812775534592;

                if (!caller.Roles.Any(r => r.Id == officerRoleId))
                {
                    await message.Channel.SendMessageAsync($"{caller.Mention} you do not have permission, Officers only.");
                    return;
                }

                if (message.MentionedUsers.Count == 0)
                {
                    await message.Channel.SendMessageAsync("Usage: `!fineuser @user amount reason`");
                    return;
                }

                var targetUser = message.MentionedUsers.First();
                var parts = message.Content.Split(' ', 4); // "!fineuser @user amount reason"

                if (parts.Length < 4)
                {
                    await message.Channel.SendMessageAsync("Usage: `!fineuser @user amount reason`");
                    return;
                }

                if (!int.TryParse(parts[2], out int amount) || amount <= 0)
                {
                    await message.Channel.SendMessageAsync("❌ invalid amount.");
                    return;
                }

                string notes = parts[3];

                var memberService = _services.GetService<IMemberService>();
                var fineService = _services.GetService<IFineService>();

                var member = await memberService.GetMemberByDiscordIdAsync(targetUser.Id.ToString());

                if (member == null)
                {
                    await message.Channel.SendFileAsync($"❌ <@{targetUser.Id}> is not registered");
                    return;
                }

                await fineService.AddEventFineAsync(member, amount, notes);

                await message.Channel.SendMessageAsync(
                    $"💸 **Fine Issued**:\n" +
                    $"User: <@{targetUser.Id}>\n" +
                    $"Amount: **{amount:N0}**\n" +
                    $"Reason: *{notes}*\n" +
                    $"Type: **Event Fine**"
                   );


                //Send dm to user
                try
                {
                    var user = _client.GetUser(targetUser.Id);
                    if (user != null)
                    {
                        var dm = await user.CreateDMChannelAsync();
                        await dm.SendMessageAsync(
                            $"⚠️ **You have received an Event Fine!**\n\n" +
                            $"**Amount:** {amount:N0}\n" +
                            $"**Reason:** {notes}\n" +
                            $"You must pay this fine in <#1440431172160061450>.\n" +
                            $"Upload your payment screenshot and the bot will process it."
                        );
                    }
                }
                catch { }

                return;

            }

            // =========================================
            // !finereign @user amount reason
            //==========================================

            if (message.Content.StartsWith("!finereign", StringComparison.OrdinalIgnoreCase))
            {
                if (message.Channel is not SocketGuildChannel guildChannel)
                {
                    await message.Channel.SendMessageAsync("This command must be used in the server.");
                    return;
                }

                var caller = message.Author as SocketGuildUser;
                ulong officerRoleId = 1222665812775534592;

                if (!caller.Roles.Any(r => r.Id == officerRoleId))
                {
                    await message.Channel.SendMessageAsync($"{caller.Mention} you do not have permission. Officers only.");
                    return;
                }

                if (message.MentionedUsers.Count == 0)
                {
                    await message.Channel.SendMessageAsync("Usage: `!finereign @user amount reason`");
                    return;
                }

                var targetUser = message.MentionedUsers.First();
                var parts = message.Content.Split(' ', 4);

                if (parts.Length < 4)
                {
                    await message.Channel.SendMessageAsync("Usage: `!finereign @user amount reason`");
                    return;
                }

                if (!int.TryParse(parts[2], out int amount) || amount <= 0)
                {
                    await message.Channel.SendMessageAsync("❌ Invalid amount.");
                    return;
                }

                string notes = parts[3];

                var memberService = _services.GetService<IMemberService>();
                var fineService = _services.GetService<IFineService>();

                var member = await memberService.GetMemberByDiscordIdAsync(targetUser.Id.ToString());
                if (member == null)
                {
                    await message.Channel.SendMessageAsync($"❌ <@{targetUser.Id}> is not registered.");
                    return;
                }

                await fineService.AddReignFineAsync(member, amount, notes);

                await message.Channel.SendMessageAsync(
                    $"⚔️ **Reign Fine Issued**:\n" +
                    $"User: <@{targetUser.Id}>\n" +
                    $"Amount: **{amount:N0}**\n" +
                    $"Reason: *{notes}*\n" +
                    $"Strikes added: **2**"
                );

                // =========== SEND DM HERE ===========
                try
                {
                    var user = _client.GetUser(targetUser.Id);
                    if (user != null)
                    {
                        var dm = await user.CreateDMChannelAsync();
                        await dm.SendMessageAsync(
                            $"⚠️ **You have received a Reign Fine!**\n\n" +
                            $"**Amount:** {amount:N0}\n" +
                            $"**Reason:** {notes}\n" +
                            $"**Strikes Added:** 2\n\n" +
                            $"🚫 You are **blacklisted from the next two Reign events**.\n" +
                            $"Strikes reduce automatically when officers lock Reign.\n\n" +
                            $"Please pay this fine in <#1440431172160061450>.\n" +
                            $"Upload your screenshot and the bot will process it."
                        );
                    }
                }
                catch { }

                return;
            }

            // ============================
            // !finelist
            // ============================

            if (message.Content.Equals("!finelist", StringComparison.OrdinalIgnoreCase))
            {
                if (message.Channel is not SocketGuildChannel listChan)
                {
                    await message.Channel.SendMessageAsync("This must be used in the server.");
                    return;
                }

                var caller = message.Author as SocketGuildUser;
                ulong officerRoleId = 1222665812775534592;

                if (!caller.Roles.Any(r => r.Id == officerRoleId))
                {
                    await message.Channel.SendMessageAsync($"{caller.Mention} you do not have permission.");
                    return;
                }

                var fineService = _services.GetService<IFineService>();
                var unpaid = await fineService.GetUnpaidFinesAsync();
                var paid = await fineService.GetPaidFinesAsync();

                string msg = "💀 **FINE LIST**\n\n";

                msg += "🟥 **UNPAID FINES**\n";
                if (unpaid.Count == 0)
                    msg += "• None\n";
                else
                {
                    foreach (var f in unpaid)
                    {
                        msg +=
                            $"• **{f.IngameName}** — {f.Amount:N0} — `{f.FineType}` — FineID `{f.FineId}` — Paid: {f.PaidAmount:N0}/{f.Amount:N0}\n";
                    }
                }

                msg += "\n🟩 **PAID (Awaiting Removal)**\n";
                if (paid.Count == 0)
                    msg += "• None\n";
                else
                {
                    foreach (var f in paid)
                    {
                        msg +=
                            $"• **{f.IngameName}** — {f.Amount:N0} — `{f.FineType}` — FineID `{f.FineId}` — PAID\n";
                    }
                }

                await SendLongMessageAsync(message.Channel, msg);
                return;
            }

            // ============================
            // !removefine FineId
            // ============================
            if (message.Content.StartsWith("!removefine", StringComparison.OrdinalIgnoreCase))
            {
                if (message.Channel is not SocketGuildChannel remChan)
                {
                    await message.Channel.SendMessageAsync("This must be used in the server.");
                    return;
                }

                var caller = message.Author as SocketGuildUser;
                ulong officerRoleId = 1222665812775534592;

                if (!caller.Roles.Any(r => r.Id == officerRoleId))
                {
                    await message.Channel.SendMessageAsync($"{caller.Mention} you do not have permission.");
                    return;
                }

                var parts = message.Content.Split(' ', 2);
                if (parts.Length < 2)
                {
                    await message.Channel.SendMessageAsync("Usage: `!removefine FineId`");
                    return;
                }

                string fineId = parts[1].Trim();
                var fineService = _services.GetService<IFineService>();

                await fineService.RemoveFineAsync(fineId);

                await message.Channel.SendMessageAsync($"🗑️ Removed fine `{fineId}`.");
                return;
            }

            if (message.Content.Equals("!myfines", StringComparison.OrdinalIgnoreCase))
            {
                var fineService = _services.GetService<IFineService>();
                var fines = await fineService.GetFinesForUserAsync(message.Author.Id.ToString());

                if (fines.Count == 0)
                {
                    await message.Channel.SendMessageAsync("🎉 You have no fines!");
                    return;
                }

                string unpaidText = "";
                string paidText = "";
                int totalOwed = 0;

                foreach (var f in fines)
                {
                    if (!f.IsPaid)
                    {
                        unpaidText += $"• **{f.Amount:N0}** — {f.FineType} — FineID `{f.FineId}` — Paid {f.PaidAmount:N0}/{f.Amount:N0}\n";
                        totalOwed += (f.Amount - f.PaidAmount);
                    }
                    else
                    {
                        paidText += $"• **{f.Amount:N0}** — {f.FineType} — FineID `{f.FineId}` — PAID\n";
                    }
                }

                string msg =
                    $"🧾 **Your Fines**\n\n" +
                    "🟥 **Unpaid**\n" +
                    (string.IsNullOrEmpty(unpaidText) ? "• None\n" : unpaidText) +
                    $"\n**Total owed: {totalOwed:N0}**\n\n" +
                    "🟩 **Paid (Awaiting removal)**\n" +
                    (string.IsNullOrEmpty(paidText) ? "• None\n" : paidText);

                await SendLongMessageAsync(message.Channel, msg);
                return;
            }

            ulong FINE_CHANNEL_ID = 1440431172160061450;

            if (message.Channel.Id == FINE_CHANNEL_ID && message.Attachments.Count > 0)
            {
                var fineService = _services.GetService<IFineService>();
                var ocrService = _services.GetService<PaddleOcrServerService>();
                var memberService = _services.GetService<IMemberService>();

                string uploaderId = message.Author.Id.ToString();
                string targetDiscordId = uploaderId;
                string targetIngameName = null;

                // payfor override (same logic as donation)
                if (_payForSessions.TryGetValue(message.Author.Id, out string overrideTarget))
                {
                    if (overrideTarget.StartsWith("DISCORD:"))
                        targetDiscordId = overrideTarget.Substring("DISCORD:".Length);
                    else if (overrideTarget.StartsWith("NAME:"))
                        targetIngameName = overrideTarget.Substring("NAME:".Length);

                    _payForSessions.Remove(message.Author.Id);
                }

                Member targetMember;

                if (targetIngameName != null)
                {
                    var allMembers = await memberService.GetAllMembersAsync();
                    targetMember = allMembers.FirstOrDefault(m =>
                        m.IngameName.Equals(targetIngameName, StringComparison.OrdinalIgnoreCase));
                }
                else
                {
                    targetMember = await memberService.GetMemberByDiscordIdAsync(targetDiscordId);
                }

                if (targetMember == null)
                {
                    await message.Channel.SendMessageAsync(
                        $"{message.Author.Mention} ❌ Target user not found.");
                    return;
                }

                int totalAmount = 0;

                foreach (var att in message.Attachments)
                {
                    if (att.ContentType == null || !att.ContentType.StartsWith("image"))
                        continue;

                    string tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");

                    using (var client = new HttpClient())
                    {
                        var data = await client.GetByteArrayAsync(att.Url);
                        await File.WriteAllBytesAsync(tmp, data);
                    }

                    int? amount = await ocrService.ExtractDonationAmountAsync(tmp);
                    File.Delete(tmp);

                    if (amount != null)
                        totalAmount += amount.Value;
                }

                if (totalAmount <= 0)
                {
                    await message.Channel.SendMessageAsync(
                        $"{message.Author.Mention} ❌ I could not read a valid fine payment amount.");
                    return;
                }

                await fineService.AddPaymentAsync(targetMember.DiscordUserId, totalAmount);

                await message.AddReactionAsync(new Emoji("💸"));
                await message.Channel.SendMessageAsync(
                    $"💰 {message.Author.Mention} paid **{totalAmount:N0}** toward fines for **{targetMember.IngameName}**.");

                return;
            }


            #endregion


            #region OTHER SIMPLE COMMANDS

            // ============================
            // !myinfo – Show user profile
            // ============================

            if (message.Content.Equals("!myinfo", StringComparison.OrdinalIgnoreCase))
            {
                var memberService = _services.GetService<IMemberService>();
                var donationService = _services.GetService<IDonationService>();

                string discordId = message.Author.Id.ToString();
                var member = await memberService.GetMemberByDiscordIdAsync(discordId);

                if (member == null)
                {
                    await message.Channel.SendMessageAsync(
                        $"{message.Author.Mention} you are not registered. Use `!register` first.");
                    return;
                }

                // weekly donation status
                int paid = await donationService.GetTotalForUserThisWeekAsync(discordId);
                int goal = 44_000_000;
                string donationStatus = member.IsExempt
                    ? "🟦 EXEMPT"
                    : (paid >= goal ? $"✅ PAID ({paid:N0} / 44,000,000)"
                                    : $"❌ UNPAID ({paid:N0} / 44,000,000)");

                string txt =
                    $"🧾 **Your Tribe Profile**\n" +
                    $"============================\n\n" +
                    $"**In-game Name:** {member.IngameName}\n" +
                    $"**In-game ID:** {member.IngameId}\n" +
                    $"**Might:** {member.Might:N0}\n" +
                    $"**Kill Points:** {member.KillPoints:N0}\n" +
                    $"**Collector Level:** {member.CollectorLevel}\n" +
                    $"**Reign Points:** {member.ReignPoints}\n" +
                    $"**Exempt:** {(member.IsExempt ? "Yes" : "No")}\n" +
                    $"**Last Updated:** {member.LastUpdatedUTC:yyyy-MM-dd HH:mm} UTC\n\n" +
                    $"💰 **Weekly Donation Status**\n" +
                    $"{donationStatus}\n\n" +
                    $"To update your info, use:\n" +
                    "`!updateigname`, `!updateid`, `!updatemight`, `!updatekills`, `!updatecollector`";

                await SendLongMessageAsync(message.Channel, txt);
                return;
            }


            // ============================
            // !viewinfo @User (OFFICERS ONLY)
            // ============================
            if (message.Content.StartsWith("!viewinfo", StringComparison.OrdinalIgnoreCase))
            {
                if (message.Channel is not SocketGuildChannel viewChan)
                {
                    await message.Channel.SendMessageAsync("This command can only be used inside the server.");
                    return;
                }

                var caller = message.Author as SocketGuildUser;
                ulong officerRoleId = 1222665812775534592;

                if (!caller.Roles.Any(r => r.Id == officerRoleId))
                {
                    await message.Channel.SendMessageAsync($"{caller.Mention} you do not have permission. Officers only.");
                    return;
                }

                if (message.MentionedUsers.Count == 0)
                {
                    await message.Channel.SendMessageAsync("Usage: `!viewinfo @user`");
                    return;
                }

                var target = message.MentionedUsers.First();
                string targetId = target.Id.ToString();

                var memberService = _services.GetService<IMemberService>();
                var donationService = _services.GetService<IDonationService>();
                var fineService = _services.GetService<IFineService>();

                var m = await memberService.GetMemberByDiscordIdAsync(targetId);

                if (m == null)
                {
                    await message.Channel.SendMessageAsync($"❌ <@{target.Id}> is **not registered**.");
                    return;
                }

                // BANK
                int goal = 44_000_000;
                int paid = await donationService.GetTotalForUserThisWeekAsync(targetId);
                string donationStatus = m.IsExempt ? "🟦 EXEMPT"
                                       : paid >= goal ? "✅ PAID"
                                       : "❌ UNPAID";

                // FINES
                var fines = await fineService.GetFinesForUserAsync(targetId);
                var unpaid = fines.Where(f => !f.IsPaid).ToList();
                var paidFines = fines.Where(f => f.IsPaid).ToList();
                int totalOwed = unpaid.Sum(f => f.Amount - f.PaidAmount);

                string fineText = "";

                fineText += "**🟥 Unpaid Fines:**\n";
                if (unpaid.Count == 0)
                    fineText += "• None\n";
                else
                    foreach (var f in unpaid)
                        fineText += $"• {f.Amount:N0} — {f.FineType} — FineID `{f.FineId}` — Paid {f.PaidAmount:N0}/{f.Amount:N0}\n";

                fineText += $"\n**Total Owed:** {totalOwed:N0}\n\n";

                fineText += "**🟩 Paid (Awaiting Removal):**\n";
                if (paidFines.Count == 0)
                    fineText += "• None\n";
                else
                    foreach (var f in paidFines)
                        fineText += $"• {f.Amount:N0} — {f.FineType} — FineID `{f.FineId}`\n";

                string reply =
                    $"📘 **Profile for <@{target.Id}>**\n\n" +
                    $"• **In-game Name:** {m.IngameName}\n" +
                    $"• **In-game ID:** `{m.IngameId}`\n" +
                    $"• **Might:** `{m.Might:N0}`\n" +
                    $"• **Kill Points:** `{m.KillPoints:N0}`\n" +
                    $"• **Collector Level:** `{m.CollectorLevel}`\n" +
                    $"• **Reign Points:** `{m.ReignPoints}`\n" +
                    $"• **Exempt:** `{m.IsExempt}`\n\n" +
                    $"🏦 **Weekly Donation**\n" +
                    $"• Paid: **{paid:N0} / 44,000,000**\n" +
                    $"• Status: {donationStatus}\n" +
                    $"• Channel: <#1440050111353721053>\n\n" +

                    $"💀 **Fines**\n" +
                    fineText +

                    $"\n🕒 **Last Updated:** `{m.LastUpdatedUTC:yyyy-MM-dd HH:mm} UTC`";

                await message.Channel.SendMessageAsync(reply);
                return;
            }




            // ============================
            // !help — Show all commands
            // ============================

            if (message.Content.Equals("!help", StringComparison.OrdinalIgnoreCase))
            {
                string helpMsg =
                    "📘 **HOGS Tribe Bot — Command Reference**\n" +
                    "Below is a categorized list of all available commands.\n\n" +

                    "==============================\n" +
                    "🔹 **GENERAL COMMANDS**\n" +
                    "==============================\n" +
                    "`!ping` — Test bot response\n" +
                    "`/checkbot` — Check if bot is online\n\n" +

                    "==============================\n" +
                    "🟦 **REGISTRATION COMMANDS**\n" +
                    "==============================\n" +
                    "`!register` — Begin DM registration\n" +
                    "`!myinfo` — View your profile\n" +
                    "`!listmembers` — List all registered members (A–Z)\n" +
                    "`!checkbank` — Check your weekly donation status\n\n" +
                    "**Officer Only:**\n" +
                    "`!registerreminder` — DM all unregistered members\n" +
                    "`!listnonregistered` — List members missing registration\n" +
                    "`!removemember @user` — Remove a member from sheets\n\n" +

                    "==============================\n" +
                    "✏️ **UPDATE COMMANDS (Edit Your Info)**\n" +
                    "==============================\n" +
                    "`!updateigname NAME` — Update in-game name\n" +
                    "`!updateid ID` — Update in-game ID\n" +
                    "`!updatemight NUMBER` — Update Might\n" +
                    "`!updatekills NUMBER` — Update Kill Points\n" +
                    "`!updatecollector LEVEL` — Update Collector Level\n" +
                    "`!updateall` — Update your whole profile through DM\n\n" +
                    "**Officer Only:**\n" +
                    "`!viewinfo @user` — View a user's full profile\n\n" +

                    "==============================\n" +
                    "⚔️ **REIGN EVENT COMMANDS**\n" +
                    "==============================\n" +
                    "`!applyreign` — Apply for the Viking Reign event\n" +
                    "`!listreign` — Show sorted Reign applicants\n\n" +
                    "**Officer Only:**\n" +
                    "`!clearreign` — Clear the Reign list\n" +
                    "`!lockreign` — Lock Reign applications\n" +
                    "`!unlockreign` — Unlock Reign applications\n\n" +

                    "==============================\n" +
                    "💰 **BANK / DONATION COMMANDS**\n" +
                    "==============================\n" +
                    "`!banktaxlist` — Show paid / unpaid / exempt members\n" +
                    "`!bankunpaid` — Show unpaid members only\n" +
                    "`!checkbank` — Check your payment progress\n" +
                    "`!payfor <@user or name>` — Pay bank donation for someone else\n\n" +
                    "**Officer Only:**\n" +
                    "`!bankreminder` — DM all unpaid members\n\n" +

                    "==============================\n" +
                    "🧾 **BANK DONATION OCR**\n" +
                    "==============================\n" +
                    "• Upload donation screenshots in <#1440050111353721053>\n" +
                    "• Bot auto-reads screenshots via OCR\n" +
                    "• Multi-image uploads supported\n\n" +

                    "==============================\n" +
                    "💀 **FINE SYSTEM COMMANDS**\n" +
                    "==============================\n" +
                    "`!myfines` — View all your fines and amounts owed\n" +
                    "`!payfor <@user or name>` — Pay someone else's fines\n" +
                    "**Officer Only:**\n" +
                    "`!fineuser @user amount reason` — Issue an event fine\n" +
                    "`!finereign @user amount reason` — Issue a Reign fine (+2 strikes)\n" +
                    "`!finelist` — Show all unpaid and paid fines\n" +
                    "`!removefine FINEID` — Remove a fine after verification\n\n" +

                    "==============================\n" +
                    "🐗 **NOTES**\n" +
                    "==============================\n" +
                    "• All OCR-based uploads must be clear and readable\n\n" +

                    "==============================\n" +
                    "🐗 **NEED HELP?**\n" +
                    "==============================\n" +
                    "Contact an officer if something doesn't look right.\n";

                await SendLongMessageAsync(message.Channel, helpMsg);
                return;
            }


            #endregion
        }


        // ======================================================
        // SLASH COMMAND HANDLER
        // ======================================================

        private async Task HandleInteractionAsync(SocketInteraction interaction)
        {
            try
            {
                if (interaction is SocketSlashCommand cmd)
                {
                    switch (cmd.Data.Name)
                    {
                        case "checkbot":
                            await cmd.RespondAsync("Bot is online and running! ⚡");
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Interaction error: " + ex.Message);
            }
        }

        // ======================================================
        // HELPER: SPLIT LONG MESSAGES
        // ======================================================

        private async Task SendLongMessageAsync(IMessageChannel channel, string text)
        {
            const int limit = 1990;
            if (text.Length <= limit)
            {
                await channel.SendMessageAsync(text);
                return;
            }

            var lines = text.Split('\n');
            string buffer = "";

            foreach (var line in lines)
            {
                if ((buffer + line).Length > limit)
                {
                    await channel.SendMessageAsync(buffer);
                    buffer = "";
                }
                buffer += line + "\n";
            }

            if (buffer.Length > 0)
                await channel.SendMessageAsync(buffer);
        }

    }
}
#endregion
