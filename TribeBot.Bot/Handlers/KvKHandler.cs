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
    [Group("kvk", "Kingdom vs Kingdom management")]
    public class KvKHandler : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly IKvKService _kvkService;
        private readonly IKvKScheduleService _kvkScheduleService;

        // CHANGE THIS TO YOUR REAL OFFICER ROLE ID
        private const ulong OfficerRoleId = 1222665812775534592;

        public KvKHandler(
            IKvKService kvkService,
            IKvKScheduleService kvkScheduleService)
        {
            _kvkService = kvkService;
            _kvkScheduleService = kvkScheduleService;
        }

        // ======================================================
        // /kvk create
        // ======================================================
        [SlashCommand("create", "Create a new KvK event")]
        public async Task CreateKvK()
        {
            if (!IsOfficer())
            {
                await RespondAsync(
                    embed: EmbedHelper.Error("You do not have permission to use this command."),
                    ephemeral: true);
                return;
            }

            await RespondWithModalAsync<KvKCreateModal>("kvk_create");
        }

        [ModalInteraction("kvk_create", ignoreGroupNames: true)]
        public async Task HandleCreateKvK(KvKCreateModal modal)
        {
            await DeferAsync(ephemeral: true);

            if (!DateTime.TryParse(modal.StartUtc, out var startUtc) ||
                !DateTime.TryParse(modal.EndUtc, out var endUtc))
            {
                await FollowupAsync(
                    embed: EmbedHelper.Error("Invalid date format. Use: yyyy-MM-dd HH:mm (UTC)"),
                    ephemeral: true);
                return;
            }

            startUtc = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc);
            endUtc = DateTime.SpecifyKind(endUtc, DateTimeKind.Utc);

            try
            {
                var kvk = await _kvkService.CreateKvKAsync(
                    modal.Name.Trim(),
                    startUtc,
                    endUtc);

                await FollowupAsync(
                    embed: EmbedHelper.Success(
                        $"**KvK Created**\n" +
                        $"Name: {kvk.Name}\n" +
                        $"Start: {kvk.StartUtc:yyyy-MM-dd HH:mm} UTC\n" +
                        $"End: {kvk.EndUtc:yyyy-MM-dd HH:mm} UTC\n" +
                        $"ID: `{kvk.KvKId}`"),
                    ephemeral: true);
            }
            catch (Exception ex)
            {
                await FollowupAsync(
                    embed: EmbedHelper.Error(ex.Message),
                    ephemeral: true);
            }
        }

        // ======================================================
        // /kvk end
        // ======================================================
        [SlashCommand("end", "End the currently active KvK")]
        public async Task EndKvK()
        {
            if (!IsOfficer())
            {
                await RespondAsync(
                    embed: EmbedHelper.Error("You do not have permission to use this command."),
                    ephemeral: true);
                return;
            }

            await DeferAsync(ephemeral: true);

            var active = await _kvkService.GetActiveKvKAsync();
            if (active == null)
            {
                await FollowupAsync(
                    embed: EmbedHelper.Info("KvK", "There is no active KvK."),
                    ephemeral: true);
                return;
            }

            await _kvkService.EndActiveKvKAsync();

            await FollowupAsync(
                embed: EmbedHelper.Success($"**{active.Name}** has been ended."),
                ephemeral: true);
        }

        // ======================================================
        // Helpers
        // ======================================================
        private bool IsOfficer()
        {
            return Context.User is SocketGuildUser user &&
                   user.Roles.Any(r => r.Id == OfficerRoleId);
        }
    }
}
