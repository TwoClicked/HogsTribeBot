using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using TribeBot.Bot.Modals;
using TribeBot.Bot.UI;
using TribeBot.Core.Interfaces;

namespace TribeBot.Bot.Handlers
{
    [Group("kvkevent", "Kingdom vs Kingdom scheduling")]
    public class KvKScheduleHandler : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly IKvKScheduleService _kvkScheduleService;

        // CHANGE THIS TO YOUR OFFICER ROLE ID
        private const ulong OfficerRoleId = 1222665812775534592;

        public KvKScheduleHandler(IKvKScheduleService kvkScheduleService)
        {
            _kvkScheduleService = kvkScheduleService;
        }

        // ======================================================
        // /kvk add-event
        // ======================================================
        [SlashCommand("add-event", "Add a KvK gate or killing field event")]
        public async Task AddKvKEvent()
        {
            var user = Context.User as SocketGuildUser;
            if (user == null || !user.Roles.Any(r => r.Id == OfficerRoleId))
            {
                await RespondAsync(
                    embed: EmbedHelper.Error("You do not have permission to use this command."),
                    ephemeral: true);
                return;
            }

            await RespondWithModalAsync<AddKvKEventModal>("kvk_add_event");
        }

        // ======================================================
        // MODAL HANDLER
        // ======================================================
        [ModalInteraction("kvk_add_event", ignoreGroupNames: true)]
        public async Task HandleAddKvKEvent(AddKvKEventModal modal)
        {
            await DeferAsync(ephemeral: true);

            // Validate datetime
            if (!DateTime.TryParseExact(
                modal.StartTime.Trim(),
                "yyyy-MM-dd HH:mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var startUtc))
            {
                await FollowupAsync(
                    embed: EmbedHelper.Error(
                        "Invalid datetime format.\nUse: **yyyy-MM-dd HH:mm** (UTC)"),
                    ephemeral: true);
                return;
            }

            // Validate event type
            var eventType = modal.EventType.Trim().ToLower();
            if (eventType != "gate" && eventType != "killingfield")
            {
                await FollowupAsync(
                    embed: EmbedHelper.Error(
                        "Event type must be **Gate** or **KillingField**."),
                    ephemeral: true);
                return;
            }

            try
            {
                await _kvkScheduleService.AddTimedEventAsync(
                    modal.KvKId.Trim(),
                    eventType,
                    startUtc);

                await FollowupAsync(
                    embed: EmbedHelper.Success(
                        $"KvK event added.\n\n" +
                        $"**KvK ID:** `{modal.KvKId}`\n" +
                        $"**Type:** `{eventType}`\n" +
                        $"**Start:** `{startUtc:yyyy-MM-dd HH:mm} UTC`"),
                    ephemeral: true);
            }
            catch (Exception ex)
            {
                await FollowupAsync(
                    embed: EmbedHelper.Error(ex.Message),
                    ephemeral: true);
            }
        }
    }
}
