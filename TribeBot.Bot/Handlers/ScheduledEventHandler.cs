using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;
using TribeBot.Bot.UI;
using TribeBot.Core.Entities;
using TribeBot.Data.Interfaces;

namespace TribeBot.Bot.Handlers
{
    public class ScheduledEventHandler : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly IGoogleSheetsDataStore _dataStore;
        private readonly DiscordSocketClient _client;

        private const ulong OfficerRoleId = 1222665812775534592;
        private const ulong OfficerLogChannelId = 1440211043820507217;

        // TESTING: Hogs event id : 1448513656542199880 Dev test role id 1439972286877794314
        private const ulong HogsEventsRoleId = 1439972286877794314;

        public ScheduledEventHandler(IGoogleSheetsDataStore dataStore, DiscordSocketClient client)
        {
            _dataStore = dataStore;
            _client = client;
        }

        private IMessageChannel OfficerLog =>
            _client.GetChannel(OfficerLogChannelId) as IMessageChannel;

        // =====================================================================
        // SLASH COMMAND: /eventstart
        // =====================================================================
        [SlashCommand("hevent", "Schedule an event and set a reminder time.")]
        public async Task EventStart(
            [Choice("Instant (Notify Immediately)", 0)]
            [Choice("1 hour before", 1)]
            [Choice("3 hours before", 3)]
            [Choice("6 hours before", 6)]
            [Choice("12 hours before", 12)]
            [Choice("24 hours before", 24)]
            int remindin)
        {
            var user = Context.User as SocketGuildUser;

            if (user == null)
            {
                await RespondAsync("Internal Error: Guild Member intent not enabled.", ephemeral: true);
                return;
            }

            if (!user.Roles.Any(r => r.Id == OfficerRoleId))
            {
                await RespondAsync(embed: EmbedHelper.Error("You do not have permission."), ephemeral: true);
                return;
            }

            string utcNow = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm");
            string localNow = $"{DateTime.Now:yyyy-MM-dd HH:mm} ({TimeZoneInfo.Local.StandardName})";

            var modal = new ModalBuilder()
                .WithTitle("Schedule Event")
                .WithCustomId($"scheduleevt-{remindin}")
                .AddTextInput("Event Name", "eventname", TextInputStyle.Short, required: true)
                .AddTextInput("Event Date (YYYY-MM-DD)", "eventdate", TextInputStyle.Short, required: true)
                .AddTextInput("Event Time (HH:MM 24h)", "eventtime", TextInputStyle.Short, required: true)
                .AddTextInput("Current Time", "nowinfo", TextInputStyle.Short,
                    value: $"UTC: {utcNow} | Local: {localNow}", required: false)
                .AddTextInput("Custom Message", "eventmessage", TextInputStyle.Paragraph, required: true);

            await RespondWithModalAsync(modal.Build());
        }

        // =====================================================================
        // FIXED MODAL HANDLER: INSTANT REMINDER
        // =====================================================================
        [ModalInteraction("scheduleevt-0")]
        public async Task HandleInstantSchedule(ScheduleEventModal modal)
        {
            await HandleEventSchedule(modal, 0);
        }

        // =====================================================================
        // FIXED MODAL HANDLER: NUMBERED REMINDER
        // =====================================================================
        [ModalInteraction("scheduleevt-{hours:int}")]
        public async Task HandleDelayedSchedule(ScheduleEventModal modal, int hours)
        {
            await HandleEventSchedule(modal, hours);
        }

        // =====================================================================
        // SHARED EVENT SCHEDULING LOGIC
        // =====================================================================
        private async Task HandleEventSchedule(ScheduleEventModal modal, int remindIn)
        {
            await DeferAsync(ephemeral: true);

            if (!DateTime.TryParse($"{modal.EventDate} {modal.EventTime}", out DateTime localTime))
            {
                await FollowupAsync(embed: EmbedHelper.Error("Invalid date or time format."), ephemeral: true);
                return;
            }

            DateTime utcTime = TimeZoneInfo.ConvertTimeToUtc(localTime);
            string eventId = $"evt_{Guid.NewGuid():N}".Substring(0, 10);

            var evt = new ScheduledEvent
            {
                EventId = eventId,
                EventName = modal.EventName.Trim(),
                EventDateUtc = utcTime,
                ReminderOffsetHours = remindIn,
                Message = modal.CustomMessage.Trim(),
                CreatedBy = Context.User.Id.ToString(),
                CreatedAtUtc = DateTime.UtcNow,
                ReminderSent = false,
                Completed = false
            };

            await _dataStore.AddScheduledEventAsync(evt);

            if (remindIn == 0)
            {
                try
                {
                    await SendInstantReminder(evt);
                    evt.ReminderSent = true;
                    await _dataStore.UpdateScheduledEventAsync(evt);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[InstantReminderError] {ex}");
                    await FollowupAsync(embed: EmbedHelper.Error("Event saved, but instant reminder failed."), ephemeral: true);
                    return;
                }
            }

            await FollowupAsync(embed:
                EmbedHelper.Success(
                    $"Event **{evt.EventName}** scheduled!\n" +
                    $"🕒 **{evt.EventDateUtc:yyyy-MM-dd HH:mm UTC}**\n" +
                    $"⏰ Reminder: **{(remindIn == 0 ? "Instant" : $"{remindIn} hours before")}**"
                ), ephemeral: true);
        }

        // =====================================================================
        // SAFE INSTANT REMINDER
        // =====================================================================
        private async Task SendInstantReminder(ScheduledEvent evt)
        {
            var guild = _client.Guilds.FirstOrDefault();
            if (guild == null) return;

            var users = await guild.GetUsersAsync().FlattenAsync();
            var targets = users.Where(u => !u.IsBot && u.RoleIds.Contains(HogsEventsRoleId)).ToList();

            int sent = 0;
            var failed = new System.Collections.Generic.List<string>();

            var embed = BuildEventReminderEmbed(evt, isInstant: true);

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
                    failed.Add(user.Username);
                }
            }

            var log = OfficerLog;
            if (log != null)
            {
                var l = new EmbedBuilder()
                    .WithTitle("📢 Instant Reminder Sent")
                    .WithColor(Color.Orange)
                    .AddField("Event", evt.EventName)
                    .AddField("Sent", sent, true)
                    .AddField("Failed", failed.Count, true);

                if (failed.Count > 0)
                    l.AddField("Failed Users", string.Join("\n", failed));

                await log.SendMessageAsync(embed: l.Build());
            }
        }

        private Embed BuildEventReminderEmbed(ScheduledEvent evt, bool isInstant)
        {
            long unix = ((DateTimeOffset)evt.EventDateUtc).ToUnixTimeSeconds();

            return new EmbedBuilder()
                .WithTitle(isInstant ? "📢 Instant Event Notification" : "⏰ Event Reminder")
                .WithColor(new Color(0x00A3E0))
                .AddField("Event", $"**{evt.EventName}**")
                .AddField("Starts At", $"**{evt.EventDateUtc:yyyy-MM-dd HH:mm} UTC**")
                .AddField("Starts In", $"<t:{unix}:R>")
                .AddField("Message", evt.Message)
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();
        }
    }
}
