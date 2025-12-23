using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;
using TribeBot.Bot.Modals;
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
        private const ulong EventCoordinatorRoleId = 1284094048260587622;
        private const ulong HogsEventsRoleId = 1448513656542199880;

        public ScheduledEventHandler(IGoogleSheetsDataStore dataStore, DiscordSocketClient client)
        {
            _dataStore = dataStore;
            _client = client;
        }

        private IMessageChannel OfficerLog =>
            _client.GetChannel(OfficerLogChannelId) as IMessageChannel;

        // =====================================================================
        // SLASH COMMAND: /hevent
        // =====================================================================
        [SlashCommand("hevent", "Schedule an event and set a reminder time.")]
        public async Task EventStart(
            [Choice("Instant (Notify Immediately)", 0)]
            [Choice("1 hour before", 1)]
            [Choice("3 hours before", 3)]
            [Choice("6 hours before", 6)]
            [Choice("12 hours before", 12)]
            [Choice("24 hours before", 24)]
            int remindIn)
        {
            var user = Context.User as SocketGuildUser;

            if (user == null)
            {
                await RespondAsync("Internal Error: Guild Member intent not enabled.", ephemeral: true);
                return;
            }

            if (!user.Roles.Any(r => r.Id == OfficerRoleId || r.Id == EventCoordinatorRoleId))
            {
                await RespondAsync(embed: EmbedHelper.Error("You do not have permission."), ephemeral: true);
                return;
            }

            string utcNow = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm");
            string localNow = $"{DateTime.Now:yyyy-MM-dd HH:mm} ({TimeZoneInfo.Local.StandardName})";

            // FIX → Use colon in CustomId
            var modal = new ModalBuilder()
                .WithTitle("Schedule Event")
                .WithCustomId($"scheduleevt:{remindIn}")
                .AddTextInput("Event Name", "eventname", TextInputStyle.Short)
                .AddTextInput("Event Date (YYYY-MM-DD)", "eventdate", TextInputStyle.Short)
                .AddTextInput("Event Time (HH:MM 24h)", "eventtime", TextInputStyle.Short)
                .AddTextInput("Current Time", "nowinfo", TextInputStyle.Short,
                    value: $"UTC: {utcNow} | Local: {localNow}", required: false)
                .AddTextInput("Custom Message", "eventmessage", TextInputStyle.Paragraph);

            await RespondWithModalAsync(modal.Build());
        }

        // =====================================================================
        // MODAL ROUTE: Handles ALL delay values
        // =====================================================================
        [ModalInteraction("scheduleevt:{hours:int}")]
        public async Task HandleScheduleModal(ScheduleEventModal modal, int hours)
        {
            await HandleEventSchedule(modal, hours);
        }


        // =====================================================================
        // SHARED LOGIC
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
                await SendInstantReminder(evt);
                evt.ReminderSent = true;
                await _dataStore.UpdateScheduledEventAsync(evt);
            }

            await FollowupAsync(embed:
                EmbedHelper.Success(
                    $"Event **{evt.EventName}** scheduled!\n" +
                    $"🕒 **{evt.EventDateUtc:yyyy-MM-dd HH:mm UTC}**\n" +
                    $"⏰ Reminder: **{(remindIn == 0 ? "Instant" : $"{remindIn} hours before")}**"),
                ephemeral: true);
        }

        [ModalInteraction("scheduleevt:1")]
        public Task Schedule1(ScheduleEventModal modal) => HandleEventSchedule(modal, 1);

        [ModalInteraction("scheduleevt:3")]
        public Task Schedule3(ScheduleEventModal modal) => HandleEventSchedule(modal, 3);

        [ModalInteraction("scheduleevt:6")]
        public Task Schedule6(ScheduleEventModal modal) => HandleEventSchedule(modal, 6);

        [ModalInteraction("scheduleevt:12")]
        public Task Schedule12(ScheduleEventModal modal) => HandleEventSchedule(modal, 12);

        [ModalInteraction("scheduleevt:24")]
        public Task Schedule24(ScheduleEventModal modal) => HandleEventSchedule(modal, 24);

        [ModalInteraction("scheduleevt:0")]
        public Task ScheduleInstant(ScheduleEventModal modal) => HandleEventSchedule(modal, 0);


        private async Task SendInstantReminder(ScheduledEvent evt)
        {
            var guild = _client.Guilds.FirstOrDefault();
            if (guild == null) return;

            var users = await guild.GetUsersAsync().FlattenAsync();
            var targets = users.Where(u => !u.IsBot && u.RoleIds.Contains(HogsEventsRoleId));

            var embed = BuildEventReminderEmbed(evt, true);

            foreach (var u in targets)
            {
                try
                {
                    await (await u.CreateDMChannelAsync()).SendMessageAsync(embed: embed);
                    await Task.Delay(1100);
                }
                catch { }
            }
        }

        private Embed BuildEventReminderEmbed(ScheduledEvent evt, bool instant)
        {
            long unix = new DateTimeOffset(
                DateTime.SpecifyKind(evt.EventDateUtc, DateTimeKind.Utc)
            ).ToUnixTimeSeconds();

            return new EmbedBuilder()
                .WithTitle(instant ? "📢 Instant Event Notification" : "⏰ Event Reminder")
                .WithColor(Color.Blue)
                .AddField("Event", evt.EventName)
                .AddField("Starts At", $"{evt.EventDateUtc:yyyy-MM-dd HH:mm} UTC")
                .AddField("Message", evt.Message)
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();
        }

    }
}
