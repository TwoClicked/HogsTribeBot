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

        // Tracks whether pre-announcement already fired for this rotation cycle
        private readonly Dictionary<string, bool> _preAnnounced = new()
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
                        await Tick();                    // Existing event scheduler
                        await CheckTitleRotationAsync(); // NEW title rotation scheduler
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Scheduler Error: {ex}");
                    }

                    await Task.Delay(TimeSpan.FromSeconds(30));
                }
            });
        }

        // ============================================================================
        // EVENT REMINDER SYSTEM (UNCHANGED)
        // ============================================================================
        private async Task Tick()
        {
            var events = await _dataStore.GetAllScheduledEventsAsync();
            var nowUtc = DateTime.UtcNow;

            foreach (var evt in events)
            {
                if (evt.Completed) continue;

                DateTime reminderTime = evt.EventDateUtc.AddHours(-evt.ReminderOffsetHours);

                if (!evt.ReminderSent && nowUtc >= reminderTime && nowUtc < evt.EventDateUtc)
                    await SendReminder(evt);

                if (nowUtc >= evt.EventDateUtc.AddMinutes(5))
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

            ulong targetRoleId = 1439972286877794314; // dev role or real event role

            var members = await guild.GetUsersAsync().FlattenAsync();
            var targets = members.Where(u => !u.IsBot && u.RoleIds.Contains(targetRoleId)).ToList();

            int sent = 0;
            var failedUsers = new List<string>();
            var embed = BuildEventReminderEmbed(evt);

            foreach (var user in targets)
            {
                try
                {
                    var dm = await user.CreateDMChannelAsync();
                    await dm.SendMessageAsync(embed: embed);
                    sent++;
                    await Task.Delay(1100);
                }
                catch
                {
                    failedUsers.Add($"{user.Username} ({user.Id})");
                }
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
                    .AddField("EventId", evt.EventId)
                    .AddField("DMs Sent", sent.ToString(), true)
                    .AddField("DMs Failed", failedUsers.Count.ToString(), true);

                if (failedUsers.Count > 0)
                    logEmbed.AddField("Failed Users", string.Join("\n", failedUsers));

                await log.SendMessageAsync(embed: logEmbed.Build());
            }
        }

        private Embed BuildEventReminderEmbed(ScheduledEvent evt)
        {
            long unix = ((DateTimeOffset)evt.EventDateUtc).ToUnixTimeSeconds();

            return new EmbedBuilder()
                .WithTitle("⏰ Event Reminder")
                .WithColor(new Color(0x00A3E0))
                .AddField("Event", $"**{evt.EventName}**")
                .AddField("Starts At (UTC)", $"**{evt.EventDateUtc:yyyy-MM-dd HH:mm} UTC**")
                .AddField("Starts In", $"<t:{unix}:R>")
                .AddField("Message", evt.Message)
                .WithFooter($"Event ID: {evt.EventId}")
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();
        }

        // ============================================================================
        // TITLE ROTATION SYSTEM — NEW
        // ============================================================================

        private async Task CheckTitleRotationAsync()
        {
            await CheckSingleTitleRotationAsync("tycoon", Color.Blue);
            await CheckSingleTitleRotationAsync("priest", Color.Green);
        }

        private async Task CheckSingleTitleRotationAsync(string title, Color color)
        {
            string? nextRotationUtc = await _dataStore.GetNextTitleRotationUtcAsync(title);
            if (string.IsNullOrWhiteSpace(nextRotationUtc))
                return;

            if (!DateTime.TryParse(nextRotationUtc, out DateTime rotationTime))
                return;

            var queue = await _dataStore.GetTitleQueueAsync(title);
            if (queue.Count == 0)
                return;

            string nextUserId = queue[0].DiscordUserId;

            DateTime now = DateTime.UtcNow;
            DateTime preAnnounceTime = rotationTime.AddHours(-1);

            // PRE-ANNOUNCEMENT LOGIC
            if (now >= preAnnounceTime && now < rotationTime)
            {
                if (!_preAnnounced[title])
                {
                    await SendTitleAnnouncementAsync(title, nextUserId, queue, color, isPreAnnouncement: true);
                    _preAnnounced[title] = true;

                    await LogTitleActionAsync(title.ToUpper(), "Pre-announcement sent.");
                }
                return;
            }

            // OVERDUE ANNOUNCEMENT
            if (now >= rotationTime)
            {
                await SendTitleAnnouncementAsync(title, nextUserId, queue, color, isPreAnnouncement: false);

                await LogTitleActionAsync(title.ToUpper(), "Overdue announcement sent.");
            }
        }

        private async Task SendTitleAnnouncementAsync(
            string title,
            string nextUserId,
            List<TitleApplicant> queue,
            Color color,
            bool isPreAnnouncement)
        {
            var channel = _client.GetChannel(PurpleTitleChannelId) as IMessageChannel;
            if (channel == null)
                return;

            var guild = _client.Guilds.FirstOrDefault();
            var nextUser = guild?.GetUser(ulong.Parse(nextUserId));
            string nextMention = nextUser?.Mention ?? nextUserId;

            // Build queue list
            List<string> list = new();
            foreach (var (app, idx) in queue.Select((a, i) => (a, i + 1)))
            {
                if (idx == 1) continue;
                var u = guild?.GetUser(ulong.Parse(app.DiscordUserId));
                list.Add($"{idx}. {(u?.Username ?? app.DiscordUserId)}");
            }

            string queueText = list.Count == 0 ? "_No further applicants_" : string.Join("\n", list);

            var embed = new EmbedBuilder()
                .WithTitle(isPreAnnouncement
                    ? (title == "tycoon" ? "🎩 TYCOON — Pre-Announcement" : "✝️ PRIEST — Pre-Announcement")
                    : (title == "tycoon" ? "🎩 TYCOON — Overdue Announcement" : "✝️ PRIEST — Overdue Announcement"))
                .WithColor(color)
                .AddField("Next Up", nextMention)
                .AddField("Queue", queueText)
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();

            await channel.SendMessageAsync($"<@&{TitleGiverRoleId}> {nextMention}", embed: embed);
        }

        private async Task LogTitleActionAsync(string title, string action)
        {
            var log = _client.GetChannel(OfficerLogChannelId) as IMessageChannel;
            if (log == null) return;

            var embed = new EmbedBuilder()
                .WithTitle($"📢 Title Rotation — {title}")
                .WithDescription(action)
                .WithColor(Color.Orange)
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();

            await log.SendMessageAsync(embed: embed);
        }
    }
}
