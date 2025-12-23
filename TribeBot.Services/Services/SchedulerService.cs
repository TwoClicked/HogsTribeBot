using Discord;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using TribeBot.Core.Entities;
using TribeBot.Data.Interfaces;

namespace TribeBot.Bot.Services
{
    public class SchedulerService
    {
        private readonly DiscordSocketClient _client;
        private readonly IGoogleSheetsDataStore _dataStore;

        private const ulong OfficerLogChannelId = 1440211043820507217;
        private const ulong PurpleTitleChannelId = 1448989138610028554;
        private const ulong TitleGiverRoleId = 1448989731051540582;

        // STATIC so handlers can reset flags
        public static Dictionary<string, bool> PreAnnounced = new()
        {
            { "tycoon", false },
            { "priest", false }
        };

        public SchedulerService(DiscordSocketClient client, IGoogleSheetsDataStore dataStore)
        {
            _client = client;
            _dataStore = dataStore;
        }

        public void Start()
        {
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        await Tick();
                        await CheckTitleRotationAsync();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Scheduler Error: {ex}");
                    }

                    await Task.Delay(TimeSpan.FromHours(1));
                }
            });
        }

        // --------------------- EVENT SYSTEM ---------------------
        private async Task Tick()
        {
            var events = await _dataStore.GetAllScheduledEventsAsync();
            var now = DateTime.UtcNow;

            foreach (var evt in events)
            {
                if (evt.Completed) continue;

                DateTime reminderTime = evt.EventDateUtc.AddHours(-evt.ReminderOffsetHours);

                if (!evt.ReminderSent && now >= reminderTime && now < evt.EventDateUtc)
                    await SendReminder(evt);

                if (now >= evt.EventDateUtc.AddMinutes(5))
                {
                    evt.Completed = true;
                    await _dataStore.UpdateScheduledEventAsync(evt);
                }
            }
        }

        private async Task SendReminder(ScheduledEvent evt)
        {
            var guild = _client.Guilds.FirstOrDefault();
            if (guild == null) return;

            ulong targetRoleId = 1448513656542199880;

            var users = await guild.GetUsersAsync().FlattenAsync();
            var targets = users.Where(u => !u.IsBot && u.RoleIds.Contains(targetRoleId)).ToList();

            int sent = 0;
            var fails = new List<string>();
            var embed = BuildReminderEmbed(evt);

            foreach (var user in targets)
            {
                try
                {
                    await (await user.CreateDMChannelAsync()).SendMessageAsync(embed: embed);
                    sent++;
                    await Task.Delay(1100);
                }
                catch { fails.Add($"{user.Username} ({user.Id})"); }
            }

            evt.ReminderSent = true;
            await _dataStore.UpdateScheduledEventAsync(evt);

            var log = _client.GetChannel(OfficerLogChannelId) as IMessageChannel;
            if (log != null)
            {
                var logEmbed = new EmbedBuilder()
                    .WithTitle("⏰ Scheduled Reminder Sent")
                    .WithColor(Color.DarkBlue)
                    .AddField("Event", evt.EventName)
                    .AddField("Sent", sent)
                    .AddField("Failed", fails.Count);

                if (fails.Count > 0)
                    logEmbed.AddField("Failed Users", string.Join("\n", fails));

                await log.SendMessageAsync(embed: logEmbed.Build());
            }
        }

        private Embed BuildReminderEmbed(ScheduledEvent evt)
        {
            long unix = ((DateTimeOffset)evt.EventDateUtc).ToUnixTimeSeconds();

            return new EmbedBuilder()
                .WithTitle("⏰ Event Reminder")
                .WithColor(new Color(0x00A3E0))
                .AddField("Event", evt.EventName)
                .AddField("Starts At", $"{evt.EventDateUtc:yyyy-MM-dd HH:mm} UTC")
                .AddField("Starts In", $"<t:{unix}:R>")
                .AddField("Message", evt.Message)
                .Build();
        }

        // --------------------- TITLE SYSTEM ---------------------
        private async Task CheckTitleRotationAsync()
        {
            await CheckRotation("tycoon", Color.Blue);
            await CheckRotation("priest", Color.Green);
        }

        private async Task CheckRotation(string title, Color color)
        {
            string? ts = await _dataStore.GetNextTitleRotationUtcAsync(title);
            if (string.IsNullOrWhiteSpace(ts)) return;

            if (!DateTime.TryParse(ts, out DateTime rotation)) return;

            var queue = await _dataStore.GetTitleQueueAsync(title);
            if (queue.Count == 0) return;

            string nextUserId = queue[0].DiscordUserId;
            var now = DateTime.UtcNow;

            DateTime pre = rotation.AddHours(-2);

            // PRE-ANNOUNCEMENT
            if (now >= pre && now < rotation)
            {
                if (!PreAnnounced[title])
                {
                    await SendAlert(title, nextUserId, color,
                        "⏳ Pre-Announcement",
                        "1 hour remaining before title rotation.");

                    PreAnnounced[title] = true;
                }
                return;
            }

            // OVERDUE
            if (now >= rotation)
            {
                await SendAlert(title, nextUserId, color,
                    "⚠️ Rotation Overdue",
                    "Timer expired — a Title Giver must run /titlegrant now.");
            }
        }

        private async Task SendAlert(string title, string nextUserId, Color color, string header, string msg)
        {
            var channel = _client.GetChannel(PurpleTitleChannelId) as IMessageChannel;
            if (channel == null) return;

            var guild = _client.Guilds.FirstOrDefault();
            var user = guild?.GetUser(ulong.Parse(nextUserId));
            string mention = user?.Mention ?? nextUserId;

            var embed = new EmbedBuilder()
                .WithTitle($"{header} — {(title == "tycoon" ? "🎩 TYCOON" : "✝️ PRIEST")}")
                .WithColor(color)
                .AddField("Next In Line", mention)
                .AddField("Notice", msg)
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();

            await channel.SendMessageAsync($"<@&{TitleGiverRoleId}>", embed: embed);
        }
    }
}
