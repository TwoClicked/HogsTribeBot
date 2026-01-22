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
    [Group("raid", "Raid signup management")]
    public class RaidHandler : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly IRaidService _raidService;
        private const ulong OfficerRoleId = 1222665812775534592;

        public RaidHandler(IRaidService raidService)
        {
            _raidService = raidService;
        }

        [SlashCommand("create", "Create a raid signup")]
        public async Task CreateRaidAsync()
        {
            var user = Context.User as SocketGuildUser;
            if (user == null || !user.Roles.Any(r => r.Id == OfficerRoleId))
            {
                await RespondAsync(
                    embed: EmbedHelper.Error("You do not have permission to use this command."),
                    ephemeral: true);
                return;
            }

            await RespondWithModalAsync<AddRaidModal>("raid_create");
        }

        [ModalInteraction("raid_create", ignoreGroupNames: true)]
        public async Task HandleCreateRaidAsync(AddRaidModal modal)
        {
            await DeferAsync(ephemeral: true);

            if (!DateTime.TryParseExact(
                modal.StartTime.Trim(),
                "yyyy-MM-dd HH:mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var startUtc))
            {
                await FollowupAsync(
                    embed: EmbedHelper.Error("Invalid datetime format.\nUse: **yyyy-MM-dd HH:mm** (UTC)"),
                    ephemeral: true);
                return;
            }

            var raidType = modal.RaidType.Trim();
            if (string.IsNullOrWhiteSpace(raidType))
            {
                await FollowupAsync(
                    embed: EmbedHelper.Error("Raid type cannot be empty."),
                    ephemeral: true);
                return;
            }

            var embed = RaidEmbedBuilder.BuildInitial(
                modal.KvKId.Trim(),
                raidType,
                startUtc);

            var message = await Context.Channel.SendMessageAsync(
                embed: embed,
                components: RaidEmbedBuilder.BuildRaidComponents());

            await _raidService.CreateRaidAsync(
                raidType,
                startUtc,
                Context.Channel.Id,
                message.Id);

            await FollowupAsync(
                embed: EmbedHelper.Success(
                    $"Raid signup created.\n\n" +
                    $"**Type:** `{raidType}`\n" +
                    $"**Start:** `{startUtc:yyyy-MM-dd HH:mm}`"),
                ephemeral: true);
        }
    }
}
