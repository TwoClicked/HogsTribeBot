using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;
using TribeBot.Bot.UI;
using TribeBot.Data.Interfaces;
using TribeBot.Core.Entities;

namespace TribeBot.Bot.Handlers
{
    public class EventsManagementHandler : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly IGoogleSheetsDataStore _data;
        private readonly DiscordSocketClient _client;

        private const ulong OfficerRoleId = 1222665812775534592;
        private const ulong OfficerLogChannelId = 1440211043820507217;

        public EventsManagementHandler(IGoogleSheetsDataStore data, DiscordSocketClient client)
        {
            _data = data;
            _client = client;
        }

        private bool IsOfficer(SocketGuildUser user)
            => user.Roles.Any(r => r.Id == OfficerRoleId);

        // =====================================================================
        // /helist
        // =====================================================================
        [SlashCommand("helist", "View all upcoming scheduled events.")]
        public async Task ListEvents()
        {
            var events = await _data.GetAllScheduledEventsAsync();
            var upcoming = events.Where(e => !e.Completed).OrderBy(e => e.EventDateUtc).ToList();

            if (!upcoming.Any())
            {
                await RespondAsync(embed: EmbedHelper.Info("Scheduled Events", "There are no upcoming events."), ephemeral: true);
                return;
            }

            var embed = new EmbedBuilder()
                .WithTitle("📅 Upcoming Events")
                .WithColor(Color.Blue);

            foreach (var e in upcoming)
            {
                embed.AddField(
                    $"{e.EventName} — `{e.EventId}`",
                    $"🕒 **{e.EventDateUtc:yyyy-MM-dd HH:mm} UTC**\n" +
                    $"⏰ Reminder: **{e.ReminderOffsetHours} hours before**\n" +
                    $"👤 Created by: <@{e.CreatedBy}>");
            }

            await RespondAsync(embed: embed.Build(), ephemeral: true);
        }

        // =====================================================================
        // /hedelete — OPEN STATIC MODAL
        // =====================================================================
        [SlashCommand("hedelete", "Delete a scheduled event by ID.")]
        public async Task DeleteEvent(string eventid)
        {
            var user = Context.Guild.GetUser(Context.User.Id);
            if (!IsOfficer(user))
            {
                await RespondAsync(embed: EmbedHelper.Error("Only officers may delete events."), ephemeral: true);
                return;
            }

            // STATIC CustomId — required for Discord.Net 3.12
            var modal = new ModalBuilder()
                .WithTitle("Delete Event")
                .WithCustomId("deleteevt")       // STATIC ID
                .AddTextInput("Confirm Event ID", "confirmtext", TextInputStyle.Short)
                .AddTextInput("Hidden Event ID", "eventid", TextInputStyle.Short, value: eventid); // hidden field

            await RespondWithModalAsync(modal.Build());
        }

        // =====================================================================
        // DELETE HANDLER — STATIC MODAL ID
        // =====================================================================
        [ModalInteraction("deleteevt")]
        public async Task HandleDeleteEvent(DeleteEventModal modal)
        {
            Console.WriteLine("[DEBUG] DeleteEvent handler reached.");

            await DeferAsync(ephemeral: true);

            string eventId = modal.EventId; // comes from hidden input

            if (modal.ConfirmText != eventId)
            {
                await FollowupAsync(embed: EmbedHelper.Error("Event ID mismatch."), ephemeral: true);
                return;
            }

            var evt = (await _data.GetAllScheduledEventsAsync())
                .FirstOrDefault(e => e.EventId == eventId);

            if (evt == null)
            {
                await FollowupAsync(embed: EmbedHelper.Error("Event not found."), ephemeral: true);
                return;
            }

            evt.Completed = true;
            evt.ReminderSent = true;
            await _data.UpdateScheduledEventAsync(evt);

            await FollowupAsync(embed: EmbedHelper.Success("Event deleted!"), ephemeral: true);
        }

        // =====================================================================
        // /heedit — OPEN STATIC MODAL
        // =====================================================================
        [SlashCommand("heedit", "Edit a scheduled event.")]
        public async Task EditEvent(string eventid)
        {
            var user = Context.Guild.GetUser(Context.User.Id);
            if (!IsOfficer(user))
            {
                await RespondAsync(embed: EmbedHelper.Error("Only officers may edit events."), ephemeral: true);
                return;
            }

            var evt = (await _data.GetAllScheduledEventsAsync())
                .FirstOrDefault(e => e.EventId == eventid);

            if (evt == null)
            {
                await RespondAsync(embed: EmbedHelper.Error("Event not found."), ephemeral: true);
                return;
            }

            string utcNow = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm");

            var modal = new ModalBuilder()
                .WithTitle($"Edit Event — {evt.EventName}")
                .WithCustomId("editevt")       // STATIC ID
                .AddTextInput("Event Name", "editname", value: evt.EventName)
                .AddTextInput("Event Date (YYYY-MM-DD)", "editdate", value: evt.EventDateUtc.ToLocalTime().ToString("yyyy-MM-dd"))
                .AddTextInput("Event Time (HH:MM)", "edittime", value: evt.EventDateUtc.ToLocalTime().ToString("HH:mm"))
                .AddTextInput("Message", "editmessage", TextInputStyle.Paragraph, value: evt.Message)
                .AddTextInput("Hidden Event ID", "eventid", TextInputStyle.Short, value: eventid); // hidden ID

            await RespondWithModalAsync(modal.Build());
        }

        // =====================================================================
        // EDIT HANDLER — STATIC MODAL ID
        // =====================================================================
        [ModalInteraction("editevt")]
        public async Task HandleEditEvent(EditEventModal modal)
        {
            Console.WriteLine("[DEBUG] EditEvent handler reached.");

            await DeferAsync(ephemeral: true);

            string eventId = modal.EventId;

            var evt = (await _data.GetAllScheduledEventsAsync())
                .FirstOrDefault(e => e.EventId == eventId);

            if (evt == null)
            {
                await FollowupAsync(embed: EmbedHelper.Error("Event not found."), ephemeral: true);
                return;
            }

            if (!DateTime.TryParse($"{modal.EventDate} {modal.EventTime}", out DateTime local))
            {
                await FollowupAsync(embed: EmbedHelper.Error("Invalid date/time."), ephemeral: true);
                return;
            }

            evt.EventName = modal.EventName.Trim();
            evt.Message = modal.Message.Trim();
            evt.EventDateUtc = TimeZoneInfo.ConvertTimeToUtc(local);
            evt.ReminderSent = false;

            await _data.UpdateScheduledEventAsync(evt);

            // ⭐ Send notification to all HogsEvent role members
            await NotifyEventEditedAsync(evt);

            await FollowupAsync(
                embed: EmbedHelper.Success($"Event updated!\nNew Time: **{evt.EventDateUtc:yyyy-MM-dd HH:mm} UTC**"),
                ephemeral: true
            );
        }


        private async Task NotifyEventEditedAsync(ScheduledEvent evt)
        {
            var guild = _client.Guilds.FirstOrDefault();
            if (guild == null) return;

            ulong notifyRoleId = 1448513656542199880; // HogsEvents role ID

            var members = await guild.GetUsersAsync().FlattenAsync();
            var targets = members
                .Where(u => !u.IsBot && u.RoleIds.Contains(notifyRoleId))
                .ToList();

            long unixTime = ((DateTimeOffset)evt.EventDateUtc).ToUnixTimeSeconds();

            var embed = new EmbedBuilder()
                .WithTitle("✏️ Event Updated")
                .WithColor(Color.Orange)
                .AddField("Event", evt.EventName)
                .AddField("New Time (UTC)", evt.EventDateUtc.ToString("yyyy-MM-dd HH:mm"))
                .AddField("Starts In", $"<t:{unixTime}:R>")
                .AddField("Message", evt.Message)
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();

            foreach (var user in targets)
            {
                try
                {
                    var dm = await user.CreateDMChannelAsync();
                    await dm.SendMessageAsync(embed: embed);
                    await Task.Delay(900); // prevent rate limit issues
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Edit Notify Error] Could not DM {user.Username}: {ex.Message}");
                }
            }
        }

    }
}
