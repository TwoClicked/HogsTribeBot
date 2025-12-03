using Discord;
using Discord.WebSocket;
using Google.Apis.Drive.v3.Data;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TribeBot.Core.Entities;
using TribeBot.Data.GoogleSheets;
using TribeBot.Data.Interfaces;
using TribeBot.Services.Interfaces;
using TribeBot.Services.Services;

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
            services.AddSingleton<IVoteService, VoteService>();

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
            
            // Load ReignLocked state from sheets

            var dataStore = _services.GetService<IGoogleSheetsDataStore>();
            _reignLocked = await dataStore.GetReignLockedAsync();

            Console.WriteLine($"Reign lock state loaded{_reignLocked}");


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

                            // NEW: Get stats from reminder execution
                            var summary = await SendRegistrationRemindersAsync();

                            // Log to officer log channel
                            ulong officerLogChannelId = 1440209811621937273;
                            var logChannel = _client.GetChannel(officerLogChannelId) as IMessageChannel;

                            if (logChannel != null)
                            {
                                await logChannel.SendMessageAsync(
                                    $"📣 **Daily Registration Reminder Sent**\n" +
                                    $"🕒 `{DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC`\n\n" +
                                    $"• Total HOGS members: **{summary.TotalMembers}**\n" +
                                    $"• Registered: **{summary.RegisteredMembers}**\n" +
                                    $"• Not registered: **{summary.UnregisteredMembers}**\n" +
                                    $"• DMs sent successfully: **{summary.DMsSent}**\n" +
                                    $"• DM failures: **{summary.DMFailures.Count}**\n\n" +
                                    (summary.DMFailures.Count > 0
                                        ? "⚠️ **Could not DM:**\n" +
                                          string.Join("\n", summary.DMFailures.Select(id => $"• <@{id}>"))
                                        : "✅ All unregistered members were DM'd successfully.")
                                );
                            }
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
        private async Task<ReminderSummary> SendRegistrationRemindersAsync()
        {
            var summary = new ReminderSummary();

            try
            {
                var guild = _client.GetGuild(1109193500664287336);

                var hogsRole = guild.GetRole(1222668156271591485);
                if (hogsRole == null)
                {
                    Console.WriteLine("HOGS role not found.");
                    return summary;
                }

                summary.TotalMembers = hogsRole.Members.Count();

                var memberService = _services.GetService<IMemberService>();
                var registered = await memberService.GetAllMembersAsync();
                var registeredIds = registered.Select(m => m.DiscordUserId).ToHashSet();

                summary.RegisteredMembers = registeredIds.Count;

                var unregistered = hogsRole.Members
                    .Where(u => !registeredIds.Contains(u.Id.ToString()))
                    .ToList();

                summary.UnregisteredMembers = unregistered.Count;

                if (unregistered.Count == 0)
                {
                    Console.WriteLine("All hogs have been registered.");
                    return summary;
                }

                Console.WriteLine($"Sending DM reminders to {unregistered.Count} users...");

                // Officer log channel
                ulong officerLogChannelId = 1440209811621937273;
                var officerLog = _client.GetChannel(officerLogChannelId) as IMessageChannel;

                foreach (var user in unregistered)
                {
                    try
                    {
                        var dm = await user.CreateDMChannelAsync();
                        await dm.SendMessageAsync(
                            $"👋 Hello **{user.Username}**, you still need to register with the Tribe Bot.\n" +
                            $"Please type `!register` here in DM.\n\n" +
                            $"Registration is required to participate in tribe events.");

                        summary.DMsSent++;

                        // IMPORTANT: prevent heartbeat blocking
                        await Task.Yield();
                        await Task.Delay(1200);
                    }
                    catch
                    {
                        summary.DMFailures.Add(user.Id);

                        if (officerLog != null)
                        {
                            await officerLog.SendMessageAsync(
                                $"⚠️ Could not DM <@{user.Id}> — Their DMs may be closed.");
                        }

                        // Prevent gateway thread starvation
                        await Task.Yield();
                        await Task.Delay(2000);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Reminder job failed: {ex.Message}");
            }

            return summary;
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
            // !updateReignPoints <points> @user  (OFFICERS ONLY)
            // ============================

            if (message.Content.StartsWith("!updateReignPoints ", StringComparison.OrdinalIgnoreCase))
            {
                if (message.Channel is not SocketGuildChannel)
                {
                    await message.Channel.SendMessageAsync("This command must be used in the server.");
                    return;
                }

                var caller = message.Author as SocketGuildUser;
                ulong officerRoleId = 1222665812775534592; // Officer role

                if (!caller.Roles.Any(r => r.Id == officerRoleId))
                {
                    await message.Channel.SendMessageAsync($"{caller.Mention} you do not have permission.");
                    return;
                }

                var parts = message.Content.Split(" ");

                if (parts.Length < 2 || message.MentionedUsers.Count == 0)
                {
                    await message.Channel.SendMessageAsync("Usage: `!updateReignPoints <points> @user`");
                    return;
                }

                if (!ulong.TryParse(parts[1], out ulong newPoints))
                {
                    await message.Channel.SendMessageAsync("❌ Invalid number. Example: `!updateReignPoints 150000 @user`");
                    return;
                }

                var target = message.MentionedUsers.First();
                var memberService = _services.GetService<IMemberService>();
                var member = await memberService.GetMemberByDiscordIdAsync(target.Id.ToString());

                if (member == null)
                {
                    await message.Channel.SendMessageAsync($"❌ <@{target.Id}> is not registered.");
                    return;
                }

                member.ReignPoints = newPoints;
                member.LastUpdatedUTC = DateTime.UtcNow;

                await memberService.RegisterOrUpdateAsync(member);

                await message.Channel.SendMessageAsync(
                    $"🏆 Updated **Reign Points** for <@{target.Id}> to **{newPoints:N0}**."
                );

                // Log to officer channel
                ulong officerLog = 1440209811621937273;
                var log = _client.GetChannel(officerLog) as IMessageChannel;
                if (log != null)
                    await log.SendMessageAsync(
                        $"📝 **Reign Points Updated**\n" +
                        $"• User: <@{target.Id}>\n" +
                        $"• New Points: **{newPoints:N0}**\n" +
                        $"• By: <@{caller.Id}>\n" +
                        $"• Time: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC"
                    );

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

                string args = message.Content.Substring("!removemember".Length).Trim();

                if (string.IsNullOrWhiteSpace(args))
                {
                    await message.Channel.SendMessageAsync("Usage: `!removemember @user` or !removemember ingameName`");
                    return;
                }

                var memberService = _services.GetService<IMemberService>();
                var dataStore = _services.GetService<IGoogleSheetsDataStore>();

                Member? memberToRemove = null;

                // CASE 1: remove by discord id
                if (message.MentionedUsers.Count > 0)
                {
                    var targetUser = message.MentionedUsers.First();
                    memberToRemove = await memberService.GetMemberByDiscordIdAsync(targetUser.Id.ToString());
                }
                else
                {
                    //CASE 2: Remove by ingame name
                    string ingamename = args;
                    var allMembers = await memberService.GetAllMembersAsync();

                    memberToRemove = allMembers.FirstOrDefault(m =>
                    m.IngameName.Equals(ingamename, StringComparison.OrdinalIgnoreCase));
                }

                if (memberToRemove == null)
                {
                    await message.Channel.SendMessageAsync($"❌ No registered member found matching **{args}**.");
                    return;
                }

                bool success = await dataStore.RemoveMemberByDiscordIdAsync(memberToRemove.DiscordUserId);

                if (success)
                    await message.Channel.SendMessageAsync($"✅ Removed **{memberToRemove.IngameName}** from the member list.");
                else
                    await message.Channel.SendMessageAsync($"❌ Removal failed. Member not found in the sheets.");

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
                if (message.Channel is not SocketGuildChannel)
                {
                    await message.Channel.SendMessageAsync("This command can only be used in the server.");
                    return;
                }

                var caller = message.Author as SocketGuildUser;
                ulong officerRoleId = 1222665812775534592;

                if (!caller.Roles.Any(r => r.Id == officerRoleId))
                {
                    await message.Channel.SendMessageAsync($"{caller.Mention} you do not have permission.");
                    return;
                }

                await message.Channel.SendMessageAsync("📨 Sending reminders in the background…");

                // Run the reminder OUTSIDE the gateway event
                _ = Task.Run(async () =>
                {
                    await SendRegistrationReminderManualAsync(message.Channel);
                });

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

                ulong vrChannel = 1429640756104265829; // vr-submissions channel

                // Restrict command being used outside of the two channels needed
                if (message.Channel.Id != vrChannel)
                {
                    await message.Channel.SendMessageAsync(
                        "**You can only use this command in the Vr-Submissions channel**."
                    );
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

                string output = "🏆 **Reign Applicants (Rally&Def leads will have max points as they have prio)**\n\n";

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
                    ulong VikingReignRole = 1364209274322157639;

                    if (!user.Roles.Any(r => r.Id == VikingReignRole))
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
                    ulong VikingReignRole = 1364209274322157639;

                    if (!user.Roles.Any(r => r.Id == VikingReignRole))
                    {
                        await message.Channel.SendMessageAsync(
                            $"{message.Author.Mention} You do not have permission. Officers only.");
                        return;
                    }

                    _reignLocked = true;

                    var dataStore = _services.GetService<IGoogleSheetsDataStore>();
                    await dataStore.SetReignLockedAsync(true); // <-- SAVE TO SHEETS

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
                    ulong VikingReignRole = 1364209274322157639;

                    if (!user.Roles.Any(r => r.Id == VikingReignRole))
                    {
                        await message.Channel.SendMessageAsync(
                            $"{message.Author.Mention} You do not have permission. Officers only.");
                        return;
                    }

                    _reignLocked = false;

                    var dataStore = _services.GetService<IGoogleSheetsDataStore>();
                    await dataStore.SetReignLockedAsync(false); // <-- Save to sheet

                    await message.Channel.SendMessageAsync("🔓 **Reign applications are now UNLOCKED.**");
                }
                return;
            }

            // ============================
            // !exempt @user  (OFFICERS ONLY)
            // ============================
            if (message.Content.StartsWith("!exempt", StringComparison.OrdinalIgnoreCase))
            {
                if (message.Channel is not SocketGuildChannel)
                {
                    await message.Channel.SendMessageAsync("This command must be used inside the server.");
                    return;
                }

                var caller = message.Author as SocketGuildUser;
                ulong officerRoleId = 1222665812775534592; // Officer role

                if (!caller.Roles.Any(r => r.Id == officerRoleId))
                {
                    await message.Channel.SendMessageAsync($"{caller.Mention} you do not have permission.");
                    return;
                }

                if (message.MentionedUsers.Count == 0)
                {
                    await message.Channel.SendMessageAsync("Usage: `!exempt @user`");
                    return;
                }

                var target = message.MentionedUsers.First();

                var memberService = _services.GetService<IMemberService>();
                var member = await memberService.GetMemberByDiscordIdAsync(target.Id.ToString());

                if (member == null)
                {
                    await message.Channel.SendMessageAsync($"❌ <@{target.Id}> is not registered.");
                    return;
                }

                // Update IsExempt
                member.IsExempt = true;
                member.LastUpdatedUTC = DateTime.UtcNow;
                await memberService.RegisterOrUpdateAsync(member);

                await message.Channel.SendMessageAsync(
                    $"🟦 <@{target.Id}> is now **EXEMPT** from weekly donations."
                );

                // Log to officer channel
                ulong officerLogChannelId = 1440209811621937273;
                var logChannel = _client.GetChannel(officerLogChannelId) as IMessageChannel;

                if (logChannel != null)
                {
                    await logChannel.SendMessageAsync(
                        $"📝 **Donation Exemption Updated**\n" +
                        $"• User: <@{target.Id}>\n" +
                        $"• Status: **EXEMPT**\n" +
                        $"• By: <@{caller.Id}>\n" +
                        $"• Time: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC"
                    );
                }

                return;
            }

            // ============================
            // !unexempt @user  (OFFICERS ONLY)
            // ============================
            if (message.Content.StartsWith("!unexempt", StringComparison.OrdinalIgnoreCase))
            {
                if (message.Channel is not SocketGuildChannel)
                {
                    await message.Channel.SendMessageAsync("This command must be used inside the server.");
                    return;
                }

                var caller = message.Author as SocketGuildUser;
                ulong officerRoleId = 1222665812775534592; // Officer role

                if (!caller.Roles.Any(r => r.Id == officerRoleId))
                {
                    await message.Channel.SendMessageAsync($"{caller.Mention} you do not have permission.");
                    return;
                }

                if (message.MentionedUsers.Count == 0)
                {
                    await message.Channel.SendMessageAsync("Usage: `!unexempt @user`");
                    return;
                }

                var target = message.MentionedUsers.First();

                var memberService = _services.GetService<IMemberService>();
                var member = await memberService.GetMemberByDiscordIdAsync(target.Id.ToString());

                if (member == null)
                {
                    await message.Channel.SendMessageAsync($"❌ <@{target.Id}> is not registered.");
                    return;
                }

                // Remove exempt status
                member.IsExempt = false;
                member.LastUpdatedUTC = DateTime.UtcNow;
                await memberService.RegisterOrUpdateAsync(member);

                await message.Channel.SendMessageAsync(
                    $"🟥 <@{target.Id}> is now **NOT EXEMPT** from weekly donations."
                );

                // Log to officer channel
                ulong officerLogChannelId = 1440209811621937273;
                var logChannel = _client.GetChannel(officerLogChannelId) as IMessageChannel;

                if (logChannel != null)
                {
                    await logChannel.SendMessageAsync(
                        $"📝 **Donation Exemption Removed**\n" +
                        $"• User: <@{target.Id}>\n" +
                        $"• Status: **NOT EXEMPT**\n" +
                        $"• By: <@{caller.Id}>\n" +
                        $"• Time: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC"
                    );
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

                    response += $"**{m.IngameName}** — {status}\n";
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

                var unpaid = members
                    .Where(m => !m.IsExempt &&
                                (!totals.ContainsKey(m.DiscordUserId) || totals[m.DiscordUserId] <= 0))
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
            // !checkbank  — simplified paid/unpaid
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

                // Exempt users
                if (member.IsExempt)
                {
                    await message.Channel.SendMessageAsync(
                        $"{message.Author.Mention} 🟦 **You are exempt from weekly donations.**");
                    return;
                }

                // Did this user pay at least once this week?
                var donations = await donationService.GetTotalForUserThisWeekAsync(id);

                if (donations > 0)
                {
                    // Paid ✔
                    await message.Channel.SendMessageAsync(
                        $"{message.Author.Mention} 🎉 **You have PAID your donation this week!**\n" +
                        $"Thank you for supporting the tribe 🐗💙");
                }
                else
                {
                    // Not paid ❌
                    await message.Channel.SendMessageAsync(
                        $"{message.Author.Mention} ❌ **You have NOT paid your weekly donation.**\n" +
                        $"Please upload your screenshot in <#1440050111353721053>.");
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

                await message.Channel.SendMessageAsync("🏦 Sending bank reminders in the background…");

                _ = Task.Run(async () =>
                {
                    await SendBankReminderManualAsync(brChan);
                });

                return;
            }
            #endregion
            #region POLL VOTING SYSTEM

            // ============================
            // !pollcreate "question" YYYY-MM-DD "opt1" "opt2" "opt3"...  
            // ============================
            if (message.Content.StartsWith("!pollcreate", StringComparison.OrdinalIgnoreCase))
            {
                if (message.Channel is not SocketGuildChannel)
                {
                    await message.Channel.SendMessageAsync("Use this command inside the server.");
                    return;
                }

                var caller = message.Author as SocketGuildUser;
                ulong officerRoleId = 1222665812775534592;

                if (!caller.Roles.Any(r => r.Id == officerRoleId))
                {
                    await message.Channel.SendMessageAsync("You do not have permission. Officers only.");
                    return;
                }

                string input = message.Content;

                // ============================
                // 1. Extract quoted parts
                // ============================
                var quoted = System.Text.RegularExpressions.Regex.Matches(input, "\"([^\"]+)\"")
                    .Select(m => m.Groups[1].Value)
                    .ToList();

                if (quoted.Count < 1)
                {
                    await message.Channel.SendMessageAsync(
                        "❌ You must include a quoted question.\nExample:\n" +
                        "`!pollcreate \"Your question here\" 2025-03-01 \"Option A\" \"Option B\"`");
                    return;
                }

                string question = quoted[0];

                // ============================
                // 2. Extract the date
                // ============================
                // Remove the "!pollcreate" and question
                string afterQuestion = input.Substring(input.IndexOf(quoted[0]) + quoted[0].Length + 2).Trim();

                // First non-quoted "word" is the date
                var dateMatch = System.Text.RegularExpressions.Regex.Match(afterQuestion, @"(\d{4}-\d{2}-\d{2})");
                if (!dateMatch.Success)
                {
                    await message.Channel.SendMessageAsync("❌ Missing or invalid date. Use YYYY-MM-DD.");
                    return;
                }

                string dateStr = dateMatch.Groups[1].Value;

                if (!DateTime.TryParse(dateStr, out DateTime endDate))
                {
                    await message.Channel.SendMessageAsync("❌ Invalid date. Use YYYY-MM-DD.");
                    return;
                }

                // ============================
                // 3. Extract options (remaining quoted values)
                // ============================
                if (quoted.Count < 3)
                {
                    await message.Channel.SendMessageAsync(
                        "❌ You must include at least TWO quoted options.\nExample:\n" +
                        "`!pollcreate \"Your question\" 2025-03-01 \"Option A\" \"Option B\"`");
                    return;
                }

                var options = quoted.Skip(1).ToList(); // skip question

                string pollId = Guid.NewGuid().ToString("N").Substring(0, 8);

                // ============================
                // 4. Save poll
                // ============================
                var voteService = _services.GetService<IVoteService>();

                var poll = new PollRecord
                {
                    PollId = pollId,
                    Question = question,
                    EndDateUtc = endDate.ToUniversalTime(),
                    Options = options,
                    CreatedByDiscordId = message.Author.Id.ToString(),
                    CreatedAtUtc = DateTime.UtcNow
                };

                await voteService.CreatePollAsync(poll);

                await message.Channel.SendMessageAsync(
                    $"📊 **Poll Created!**\n" +
                    $"Poll ID: `{pollId}`\n" +
                    $"Question: **{question}**\n" +
                    $"Ends: `{endDate:yyyy-MM-dd}`\n" +
                    $"DMs sending in the background…");

                _ = Task.Run(async () =>
                {
                    await SendPollDMsAsync(poll);
                });

                return;
            }
            // ============================
            // !polllist
            // ============================

            if (message.Content.Equals("!polllist", StringComparison.OrdinalIgnoreCase))
            {
                var voteService = _services.GetService<IVoteService>();
                var polls = await voteService.GetAllPollsAsync();


                if (polls.Count == 0)
                {
                    await message.Channel.SendMessageAsync("No polls exist.");
                    return;
                }

                string output = "📋 **Poll List**\n\n";

                foreach (var poll in polls.OrderBy(p => p.EndDateUtc))
                {
                    string status = poll.EndDateUtc < DateTime.UtcNow
                           ? "❌ ENDED"
                           : "🟢 ACTIVE";


                    output +=
                        $"• ID: `{poll.PollId}` — {status}\n" +
                        $"   Q: {poll.Question}\n" +
                        $"   Ends: {poll.EndDateUtc:yyyy-MM-dd}\n\n";
                }
                await SendLongMessageAsync(message.Channel, output);
                return;
            }
            // ============================
            // !pollremove <pollId>
            // ============================

            if (message.Content.StartsWith("!pollremove", StringComparison.OrdinalIgnoreCase))
            {
                if (message.Channel is not SocketGuildChannel)
                {
                    await message.Channel.SendMessageAsync("Use this inside the server.");
                    return;
                }

                var caller = message.Author as SocketGuildUser;
                ulong officerRoleId = 1222665812775534592;

                if (!caller.Roles.Any(r => r.Id == officerRoleId))
                {
                    await message.Channel.SendMessageAsync("You do not have permission.");
                    return;
                }

                var parts = message.Content.Split(" ", 2);
                if (parts.Length < 2)
                {
                    await message.Channel.SendMessageAsync("Usage: !pollremove <pollId>");
                    return;
                }

                string pollId = parts[1].Trim();

                var voteService = _services.GetService<IVoteService>();
                await voteService.RemovePollAsync(pollId);

                await message.Channel.SendMessageAsync($"🗑️ Removed poll `{pollId}`.");
                return;
            }

            // ============================
            // !pollshow <pollId>
            // ============================

            if (message.Content.StartsWith("!pollshow", StringComparison.OrdinalIgnoreCase))
            {
                var parts = message.Content.Split(" ", 2);

                if (parts.Length < 2)
                {
                    await message.Channel.SendMessageAsync("Usage: !pollshow <pollId>");
                    return;
                }

                string pollId = parts[1].Trim();

                var voteService = _services.GetService<IVoteService>();
                var poll = await voteService.GetPollAsync(pollId);

                if (poll == null)
                {
                    await message.Channel.SendMessageAsync("Poll not found.");
                    return;
                }

                var results = await voteService.GetAnonymousResultsAsync(pollId);

                string output =
                    $"📊 **Poll: {poll.Question}**\n" +
                    $"(Ends: {poll.EndDateUtc:yyyy-MM-dd})\n\n";

                foreach (var op in poll.Options)
                {
                    int count = results.ContainsKey(op) ? results[op] : 0;
                    output += $"• **{op}** — {count} votes\n";
                }

                int total = results.Values.Sum();
                output += $"\nTotal votes: **{total}**";

                await SendLongMessageAsync(message.Channel, output);
                return;
            }
            // ============================
            // !pollofficer <pollId>
            // ============================

            if (message.Content.StartsWith("!pollofficer", StringComparison.OrdinalIgnoreCase))
            {
                if (message.Channel is not SocketGuildChannel)
                {
                    await message.Channel.SendMessageAsync("Use this inside the server.");
                    return;
                }

                var caller = message.Author as SocketGuildUser;
                ulong officerRoleId = 1222665812775534592;

                if (!caller.Roles.Any(r => r.Id == officerRoleId))
                {
                    await message.Channel.SendMessageAsync("You do not have permission.");
                    return;
                }

                var parts = message.Content.Split(" ", 2);
                if (parts.Length < 2)
                {
                    await message.Channel.SendMessageAsync("Usage: !pollofficer <pollId>");
                    return;
                }

                string pollId = parts[1].Trim();

                var voteService = _services.GetService<IVoteService>();
                var poll = await voteService.GetPollAsync(pollId);

                if (poll == null)
                {
                    await message.Channel.SendMessageAsync("Poll not found.");
                    return;
                }

                var results = await voteService.GetOfficerResultsAsync(pollId);

                string output =
                    $"📊 **Poll (Officer View): {poll.Question}**\n" +
                    $"Ends: {poll.EndDateUtc:yyyy-MM-dd}\n\n";

                foreach (var option in poll.Options)
                {
                    output += $"**{option}:**\n";

                    if (!results.ContainsKey(option) || results[option].Count == 0)
                    {
                        output += "• No votes\n\n";
                        continue;
                    }

                    foreach (var v in results[option])
                        output += $"• {v.IngameName} (<@{v.DiscordUserId}>)\n";

                    output += "\n";
                }

                await SendLongMessageAsync(message.Channel, output);
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

                // PAYFOR logic
                if (_payForSessions.TryGetValue(message.Author.Id, out string overrideTarget))
                {
                    if (overrideTarget.StartsWith("DISCORD:"))
                        targetDiscordId = overrideTarget.Substring("DISCORD:".Length);
                    else if (overrideTarget.StartsWith("NAME:"))
                        targetIngameName = overrideTarget.Substring("NAME:".Length);

                    _payForSessions.Remove(message.Author.Id);
                }

                // Resolve target member
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
                    await message.Channel.SendMessageAsync($"{message.Author.Mention} ❌ Could not find the target account.");
                    return;
                }

                // ---- PROCESS OCR ----
                int totalAmount = 0;

                foreach (var attachment in message.Attachments)
                {
                    if (attachment.ContentType == null || !attachment.ContentType.StartsWith("image"))
                        continue;

                    string tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".png");

                    using (var client = new HttpClient())
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
                    await message.Channel.SendMessageAsync($"{message.Author.Mention} I could not read any donation amount.");
                    return;
                }

                // Save donation – now always accepted
                var now = DateTime.UtcNow;
                int daysSinceMonday = ((int)now.DayOfWeek + 6) % 7;
                DateTime weekStart = now.Date.AddDays(-daysSinceMonday);
                DateTime weekEnd = weekStart.AddDays(7);

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
                    $"{message.Author.Mention} **✅ Your payment has been recorded.**"
                );


                return;
            }

            // ============================
            // !payfor <ingamename> OR !payfor @user
            // ============================
            if (message.Content.StartsWith("!payfor", StringComparison.OrdinalIgnoreCase))
            {

                ulong bankChannel = 1440050111353721053;
                ulong fineChannel = 1440431172160061450;

                // Restrict command being used outside of the two channels needed
                if (message.Channel.Id != bankChannel && message.Channel.Id != fineChannel)
                {
                    await message.Channel.SendMessageAsync(
                        "❌ You can only use `!payfor` in the **bank donation channel** or the **fine payment channel**."
                    );
                    return;
                }


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
            // =========================================
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

                // Count ALL previous VR fines (even paid)
                var allFines = await fineService.GetAllFinesAsync();
                int previousReignFines = allFines.Count(f =>
                    f.DiscordUserId == member.DiscordUserId &&
                    f.FineType == "Reign"
                );

                bool isRepeatOffense = previousReignFines >= 1;

                await fineService.AddReignFineAsync(member, amount, notes);

                // Officer confirmation message
                string officerStrikeText = isRepeatOffense
                    ? "Strikes added: **2** (repeat offense)"
                    : "No strikes added (first offense)";

                await message.Channel.SendMessageAsync(
                    $"⚔️ **Reign Fine Issued**:\n" +
                    $"User: <@{targetUser.Id}>\n" +
                    $"Amount: **{amount:N0}**\n" +
                    $"Reason: *{notes}*\n" +
                    $"{officerStrikeText}"
                );

                // ==========================
                // Send DM to user
                // ==========================
                try
                {
                    var user = _client.GetUser(targetUser.Id);
                    if (user != null)
                    {
                        var dm = await user.CreateDMChannelAsync();

                        if (isRepeatOffense)
                        {
                            await dm.SendMessageAsync(
                                $"⚠️ **You have received another Reign Fine!**\n\n" +
                                $"**Amount:** {amount:N0}\n" +
                                $"**Reason:** {notes}\n" +
                                $"**Strikes Added:** 2\n\n" +
                                $"🚫 This is a repeat offense — you are **blacklisted from the next two Reign events**.\n" +
                                $"Strikes reduce automatically when officers lock Reign.\n\n" +
                                $"Please pay this fine in <#1440431172160061450>."
                            );
                        }
                        else
                        {
                            await dm.SendMessageAsync(
                                $"⚠️ **You have received your first Reign Fine.**\n\n" +
                                $"**Amount:** {amount:N0}\n" +
                                $"**Reason:** {notes}\n\n" +
                                $"No strikes were added for this first offense.\n" +
                                $"Please avoid future violations to prevent strike penalties.\n\n" +
                                $"Please pay this fine in <#1440431172160061450>."
                            );
                        }
                    }
                }
                catch
                {
                    // ignore DM failures
                }

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
                            $"• **{f.IngameName}** — {f.Amount:N0} — `{f.FineType}` — FineID `{f.FineId}` `{f.ReignStrikes}` — PAID\n";
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
                        await System.IO.File.WriteAllBytesAsync(tmp, data);
                    }

                    int? amount = await ocrService.ExtractDonationAmountAsync(tmp);
                    System.IO.File.Delete(tmp);

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
                    $"💰 {message.Author.Mention} **An officer will check shortly and remove the fine if correct** {targetMember.IngameName}, " +
                    $"Do keep in mind if this was a reign fine, the fine will remain on your profile untill the reign (multiplier strikes) get removed (Every 3 months).");

                return;
            }

            // ===============================================
            // USER VOTE COMMAND (DM ONLY)
            // ===============================================
            if (isDM && message.Content.StartsWith("!vote ", StringComparison.OrdinalIgnoreCase))
            {
                var parts = message.Content.Split(" ");
                if (parts.Length < 2)
                {
                    await message.Channel.SendMessageAsync("Usage: `!vote <option number>`");
                    return;
                }

                if (!int.TryParse(parts[1], out int selectedOption))
                {
                    await message.Channel.SendMessageAsync("❌ Invalid number. Example: `!vote 2`");
                    return;
                }

                var voteService = _services.GetService<IVoteService>();
                var memberService = _services.GetService<IMemberService>();

                // Get the most recent active poll (simplest)
                var polls = await voteService.GetAllPollsAsync();
                var activePoll = polls
                    .Where(p => p.EndDateUtc > DateTime.UtcNow)
                    .OrderByDescending(p => p.CreatedAtUtc)
                    .FirstOrDefault();

                if (activePoll == null)
                {
                    await message.Channel.SendMessageAsync("❌ No active poll to vote in.");
                    return;
                }

                if (selectedOption < 1 || selectedOption > activePoll.Options.Count)
                {
                    await message.Channel.SendMessageAsync("❌ That option does not exist in this poll.");
                    return;
                }

                var member = await memberService.GetMemberByDiscordIdAsync(message.Author.Id.ToString());
                string choice = activePoll.Options[selectedOption - 1];

                var vote = new PollVoteRecord
                {
                    PollId = activePoll.PollId,
                    Choice = choice,
                    DiscordUserId = message.Author.Id.ToString(),
                    IngameName = member?.IngameName ?? "",
                    TimestampUtc = DateTime.UtcNow
                };

                await voteService.AddOrUpdateVoteAsync(vote);

                await message.Channel.SendMessageAsync(
                    $"🗳️ **Your vote has been recorded!**\n" +
                    $"You voted for: **{choice}**");
                return;
            }

            // ============================
            // !promote <YouTubeLink>  (DM ONLY, CONTENT CREATORS ONLY)
            // ============================
            if (isDM && message.Content.StartsWith("!promote ", StringComparison.OrdinalIgnoreCase))
            {
                ulong CONTENT_CREATOR_ROLE = 1392919560633581728;     // <-- Your role ID
                ulong PROMOTION_CHANNEL = 1440887368247939154;        // <-- Your promotion channel

                var guild = _client.GetGuild(1109193500664287336);
                var user = guild.GetUser(message.Author.Id);

                if (user == null)
                {
                    await message.Channel.SendMessageAsync("❌ You must be in the server to use this command.");
                    return;
                }

                if (!user.Roles.Any(r => r.Id == CONTENT_CREATOR_ROLE))
                {
                    await message.Channel.SendMessageAsync(
                        "❌ You must have the **Content Creator** role to use this command.");
                    return;
                }

                string link = message.Content.Substring("!promote ".Length).Trim();

                if (!link.StartsWith("http"))
                {
                    await message.Channel.SendMessageAsync("❌ Invalid link. Provide a full YouTube URL.");
                    return;
                }

                var promoChannel = _client.GetChannel(PROMOTION_CHANNEL) as IMessageChannel;
                if (promoChannel == null)
                {
                    await message.Channel.SendMessageAsync("❌ Promotion channel not found.");
                    return;
                }

                // Send promotion message
                await promoChannel.SendMessageAsync(
                    $"@everyone\n" +
                    $"📣 **New Video Drop!**\n\n" +
                    $"🔥 **Our HOGS Content Creator {message.Author.Mention} just uploaded a new video!**\n\n" +
                    $"Support them by checking it out here:\n" +
                    $"👉 **{link}**\n\n" +
                    $"👍 Like the video\n" +
                    $"💬 Leave a comment\n" +
                    $"🔔 Subscribe to not miss future uploads!\n\n" +
                    $"Let's show them some love! 🐗💥"
                );


                await message.Channel.SendMessageAsync("✅ Your promotion has been posted!");
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

                // Determine weekly paid status
                bool paidThisWeek = false;

                if (!member.IsExempt)
                {
                    var totalThisWeek = await donationService.GetTotalForUserThisWeekAsync(discordId);
                    paidThisWeek = totalThisWeek > 0;
                }

                string donationStatus =
                    member.IsExempt ? "🟦 EXEMPT" :
                    paidThisWeek ? "✅ PAID" :
                                     "❌ UNPAID";

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

                // Determine weekly donation status
                bool paidThisWeek = false;
                if (!m.IsExempt)
                {
                    var total = await donationService.GetTotalForUserThisWeekAsync(targetId);
                    paidThisWeek = total > 0;
                }

                string donationStatus =
                    m.IsExempt ? "🟦 EXEMPT" :
                    paidThisWeek ? "✅ PAID" :
                                   "❌ UNPAID";

                // FINES (unchanged)
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

                // Final officer display
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
                    $"• Status: **{donationStatus}**\n" +
                    $"• Channel: <#1440050111353721053>\n\n" +
                    $"💀 **Fines**\n" +
                    fineText +
                    $"\n🕒 **Last Updated:** `{m.LastUpdatedUTC:yyyy-MM-dd HH:mm} UTC`";

                await message.Channel.SendMessageAsync(reply);
                return;
            }


            // ============================
            // !help — NEW INTERACTIVE HELP MENU
            // ============================
            if (message.Content.Equals("!help", StringComparison.OrdinalIgnoreCase))
            {
                await message.Channel.SendMessageAsync(
                    embed: HelpEmbeds.General(),
                    components: HelpComponents.Build("general").Build()
                );
                return;
            }
            #endregion

            //------------------
            // FALLBACK (bro i don't speak chinese) 
            //----------------------

            if (message.Channel is IDMChannel)
            {
                await message.Channel.SendMessageAsync(
                    $"{message.Author.Mention} Please use `!help` to see all available commands, but if you don't understand please message BroGuruKiller for more information");
            }

        }

        // ===============================================
        // SEND POLL TO USERS (DM-only, no buttons)
        // ===============================================
        private async Task SendPollDMsAsync(PollRecord poll)
        {
            try
            {
                var guild = _client.GetGuild(1109193500664287336);
                var memberService = _services.GetService<IMemberService>();

                var members = await memberService.GetAllMembersAsync();
                var ids = members.Select(m => ulong.Parse(m.DiscordUserId)).ToList();

                //var role = guild.GetRole(1222668156271591485); // HOGS role
                var role = guild.GetRole(1439972286877794314); // HOGS role
                var targets = role.Members.Where(m => ids.Contains(m.Id)).ToList();

                int sent = 0;
                int failed = 0;

                foreach (var user in targets)
                {
                    try
                    {
                        var dm = await user.CreateDMChannelAsync();

                        // Build numbered option list
                        string optionList = "";
                        for (int i = 0; i < poll.Options.Count; i++)
                        {
                            optionList += $"{i + 1}) {poll.Options[i]}\n";
                        }

                        string msg =
                            $"📊 **New Poll**\n" +
                            $"**{poll.Question}**\n\n" +
                            "**Options:**\n" +
                            optionList + "\n" +
                            "To vote, reply **here in DM** with for example:\n" +
                            $"`!vote 1`\n\n" +
                            $"Poll ID: `{poll.PollId}`\n" +
                            $"Ends: {poll.EndDateUtc:yyyy-MM-dd}";

                        await dm.SendMessageAsync(msg);

                        sent++;
                        await Task.Delay(1200);
                    }
                    catch
                    {
                        failed++;

                        var log = _client.GetChannel(1440209811621937273) as IMessageChannel;
                        if (log != null)
                            await log.SendMessageAsync(
                                $"⚠️ Could not DM <@{user.Id}> for poll `{poll.PollId}`."
                            );

                        await Task.Delay(2000);
                    }
                }

                // ===== FINAL SUMMARY LOG =====
                var officerLog = _client.GetChannel(1440209811621937273) as IMessageChannel;
                if (officerLog != null)
                {
                    await officerLog.SendMessageAsync(
                        $"📩 **Poll DM Summary**\n" +
                        $"Poll ID: `{poll.PollId}`\n" +
                        $"Question: **{poll.Question}**\n\n" +
                        $"• DMs Sent: **{sent}**\n" +
                        $"• Failed Deliveries: **{failed}**\n" +
                        $"• Total Members Targeted: **{targets.Count}**\n" +
                        $"• Time: `{DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC`"
                    );
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Poll DM error: {ex.Message}");
            }
        }


        private async Task SendBankReminderManualAsync(SocketGuildChannel originChannel)
        {
            try
            {
                // Convert channel to IMessageChannel for sending text
                var responseChannel = (IMessageChannel)originChannel;

                var guild = originChannel.Guild;
                var memberService = _services.GetService<IMemberService>();
                var donationService = _services.GetService<IDonationService>();

                var members = await memberService.GetAllMembersAsync();
                var totals = await donationService.GetTotalsForAllUsersThisWeekAsync();

                int goal = 0;

                var unpaid = members
                    .Where(m => !m.IsExempt &&
                                (!totals.ContainsKey(m.DiscordUserId) ||
                                 totals[m.DiscordUserId] < goal))
                    .ToList();

                if (unpaid.Count == 0)
                {
                    await responseChannel.SendMessageAsync("🎉 Everyone has paid (or is exempt)!");
                    return;
                }

                int sent = 0;
                List<ulong> failed = new();

                // Officer log channel
                ulong officerLogChannelId = 1440209811621937273;
                var officerLog = _client.GetChannel(officerLogChannelId) as IMessageChannel;

                foreach (var m in unpaid)
                {
                    ulong uid = ulong.Parse(m.DiscordUserId);
                    var user = guild.GetUser(uid);

                    if (user == null)
                    {
                        failed.Add(uid);
                        continue;
                    }

                    try
                    {
                        var dm = await user.CreateDMChannelAsync();
                        await dm.SendMessageAsync(
                            $"🏦 Hello **{m.IngameName}**, this is your weekly donation reminder.\n" +
                            $"Remember the weekly total to be paid to the bank is 44,000,000**.\n" +
                            $"Please upload your screenshot in <#1440050111353721053>."
                        );

                        sent++;
                        await Task.Delay(1200); // rate-limit safe
                    }
                    catch
                    {
                        failed.Add(uid);

                        if (officerLog != null)
                        {
                            await officerLog.SendMessageAsync(
                                $"⚠️ Could not DM <@{uid}> — DMs closed or message failed.");
                        }

                        await Task.Delay(2000);
                    }
                }

                // Final summary
                string result =
                    $"📩 **Bank Reminder Summary**\n\n" +
                    $"• Players unpaid: **{unpaid.Count}**\n" +
                    $"• DMs sent successfully: **{sent}**\n" +
                    $"• Failed DMs: **{failed.Count}**";

                if (failed.Count > 0)
                {
                    result += "\n\n⚠️ **Could not DM:**\n" +
                              string.Join("\n", failed.Select(id => $"• <@{id}>"));
                }

                await responseChannel.SendMessageAsync(result);
            }
            catch (Exception ex)
            {
                var responseChannel = (IMessageChannel)originChannel;
                await responseChannel.SendMessageAsync($"❌ Error sending bank reminders: {ex.Message}");
            }
        }

        //Manual sender for registration
        private async Task SendRegistrationReminderManualAsync(IMessageChannel originChannel)
        {
            try
            {
                var guild = _client.GetGuild(1109193500664287336);

                ulong hogsRoleId = 1222668156271591485; // LIVE ROLE
                var hogsRole = guild.GetRole(hogsRoleId);

                if (hogsRole == null)
                {
                    await originChannel.SendMessageAsync("❌ HOGS role not found.");
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
                    await originChannel.SendMessageAsync("🎉 All HOGS members are already registered!");
                    return;
                }

                // Officer log channel
                ulong officerLogChannelId = 1440209811621937273;
                var officerChannel = _client.GetChannel(officerLogChannelId) as IMessageChannel;

                int sent = 0;
                List<ulong> failed = new();

                foreach (var user in unregistered)
                {
                    try
                    {
                        var dm = await user.CreateDMChannelAsync();
                        await dm.SendMessageAsync(
                            $"👋 Hello **{user.Username}**, you still need to register with the Tribe Bot.\n" +
                            $"Please type `!register` here in DM.\n\n" +
                            $"Registration is required to participate in tribe events.");

                        sent++;

                        await Task.Delay(1200); // safe delay for Discord rate limits
                    }
                    catch
                    {
                        failed.Add(user.Id);

                        if (officerChannel != null)
                            await officerChannel.SendMessageAsync(
                                $"⚠️ Could not DM <@{user.Id}> — Their DMs may be closed.");

                        await Task.Delay(2000); // longer cooldown after a failure
                    }
                }

                // Build final summary for the officer who ran the command
                string result =
                    $"📨 **Manual Registration Reminder Summary**\n\n" +
                    $"• Unregistered members: **{unregistered.Count}**\n" +
                    $"• DMs sent successfully: **{sent}**\n" +
                    $"• Failed deliveries: **{failed.Count}**";

                if (failed.Count > 0)
                {
                    result += "\n\n⚠️ **Could not DM:**\n" +
                              string.Join("\n", failed.Select(id => $"• <@{id}>"));
                }

                await originChannel.SendMessageAsync(result);
            }
            catch (Exception ex)
            {
                await originChannel.SendMessageAsync($"❌ Error sending reminders: {ex.Message}");
            }
        }

        // ======================================================
        // SLASH COMMAND HANDLER
        // ======================================================

        private async Task HandleInteractionAsync(SocketInteraction interaction)
        {
            //
            // ===== Slash Commands =====
            //
            if (interaction is SocketSlashCommand cmd)
            {
                try
                {
                    switch (cmd.Data.Name)
                    {
                        case "checkbot":
                            await cmd.RespondAsync("Bot is online and running! ⚡");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Interaction error: " + ex.Message);
                }

                return; // IMPORTANT — only leave for slash commands
            }

            // ======= HELP MENU INTERACTIONS =======
            if (interaction is SocketMessageComponent component)
            {
                string id = component.Data.CustomId;

                string current = id switch
                {
                    "general_btn" => "general",
                    "registration_btn" => "registration",
                    "bank_btn" => "bank",
                    "fines_btn" => "fines",
                    _ => component.Data.Values?.FirstOrDefault() ?? "general"
                };

                string[] order = { "general", "registration", "update", "reign", "bank", "fines", "polls", "creator" };
                int idx = Array.IndexOf(order, current);

                string target = current;

                if (id == "helpMenu")
                    target = component.Data.Values.First();

                if (id == "help_prev")
                    target = order[(idx - 1 + order.Length) % order.Length];

                if (id == "help_next")
                    target = order[(idx + 1) % order.Length];

                if (id.EndsWith("_btn"))
                    target = id.Replace("_btn", "");

                Embed embed = target switch
                {
                    "general" => HelpEmbeds.General(),
                    "registration" => HelpEmbeds.Registration(),
                    "update" => HelpEmbeds.Update(),
                    "reign" => HelpEmbeds.Reign(),
                    "bank" => HelpEmbeds.Bank(),
                    "fines" => HelpEmbeds.Fines(),
                    "polls" => HelpEmbeds.Polls(),
                    "creator" => HelpEmbeds.Creator(),
                    _ => HelpEmbeds.General()
                };

                await component.UpdateAsync(msg =>
                {
                    msg.Embed = embed;
                    msg.Components = HelpComponents.Build(target).Build();
                });

                return;
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

    public static class HelpComponents
    {
        public static ComponentBuilder Build(string selected)
        {
            var builder = new ComponentBuilder();

            var menu = new SelectMenuBuilder()
                .WithCustomId("helpMenu")
                .WithPlaceholder("Choose category...")
                .AddOption("General", "general", isDefault: selected == "general")
                .AddOption("Registration", "registration", isDefault: selected == "registration")
                .AddOption("Update", "update", isDefault: selected == "update")
                .AddOption("Reign Event", "reign", isDefault: selected == "reign")
                .AddOption("Bank", "bank", isDefault: selected == "bank")
                .AddOption("Fines", "fines", isDefault: selected == "fines")
                .AddOption("Polls", "polls", isDefault: selected == "polls")
                .AddOption("Content Creator", "creator", isDefault: selected == "creator");

            builder.WithSelectMenu(menu);

            builder.WithButton("General", "general_btn", ButtonStyle.Primary);
            builder.WithButton("Registration", "registration_btn", ButtonStyle.Primary);
            builder.WithButton("Bank", "bank_btn", ButtonStyle.Success);
            builder.WithButton("Fines", "fines_btn", ButtonStyle.Danger);

            return builder;
        }
    }


    public static class HelpEmbeds
    {
        public static Embed General() => new EmbedBuilder()
            .WithTitle("📘 General Commands")
            .AddField("/checkbot", "Check if bot is online")
            .AddField("!myinfo", "View your profile")
            .AddField("!listmembers", "List all members")
            .AddField("Officer Only", "`!viewinfo @user`,")
            .WithColor(Color.Blue)
            .Build();

        public static Embed Registration() => new EmbedBuilder()
            .WithTitle("🟦 Registration Commands")
            .AddField("!register", "Begin DM registration")
            .AddField("Officer Only", "`!registerreminder`, `!listnonregistered`, `!removemember @user`")
            .WithColor(Color.Blue)
            .Build();

        public static Embed Update() => new EmbedBuilder()
            .WithTitle("✏️ Update Commands (Place the value behind the command!)")
            .AddField("!updateigname", "Update in-game name")
            .AddField("!updateid", "Update in-game ID")
            .AddField("!updatemight", "Update Might")
            .AddField("!updatekills", "Update kill points")
            .AddField("!updatecollector", "Update collector level")
            .AddField("!updateall", "Update all fields again")
            .AddField("Officer Only", "`!viewinfo @user`, `!updateReignPoints @user points`")
            .WithColor(Color.Gold)
            .Build();

        public static Embed Reign() => new EmbedBuilder()
            .WithTitle("⚔️ Reign Event Commands")
            .AddField("!applyreign", "Apply for Viking Reign")
            .AddField("!listreign", "Show sorted applicants")
            .AddField("Officer Only", "`!clearreign`, `!lockreign`, `!unlockreign`, `!updatereignpoints`")
            .WithColor(Color.DarkRed)
            .Build();

        public static Embed Bank() => new EmbedBuilder()
            .WithTitle("💰 Bank / Donation Commands")
            .AddField("!banktaxlist", "Show paid/unpaid members")
            .AddField("!bankunpaid", "Show unpaid members only")
            .AddField("!checkbank", "Check your donation progress")
            .AddField("!payfor", "Pay for someone else")
            .AddField("Officer Only", "`!bankreminder` `!exempt`, `!unexempt`")
            .WithColor(Color.Green)
            .Build();

        public static Embed Fines() => new EmbedBuilder()
            .WithTitle("💀 Fine System Commands")
            .AddField("!myfines", "View your fines")
            .AddField("Officer Only", "`!fineuser`, `!finereign`, `!finelist`, `!removefine`")
            .WithColor(Color.DarkGrey)
            .Build();

        public static Embed Polls() => new EmbedBuilder()
            .WithTitle("📊 Poll Commands")
            .AddField("User:", "`!polllist`, `!pollshow`, `!vote`")
            .AddField("Officer:", "`!pollcreate`, `!pollremove`, `!pollofficer`")
            .WithColor(Color.Purple)
            .Build();

        public static Embed Creator() => new EmbedBuilder()
            .WithTitle("🎥 Content Creator")
            .AddField("!promote", "Send your YouTube video to the promo channel")
            .WithColor(Color.Magenta)
            .Build();
    }

}
#endregion
