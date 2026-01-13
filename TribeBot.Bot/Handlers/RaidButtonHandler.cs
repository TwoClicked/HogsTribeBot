using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using System;
using System.Threading.Tasks;
using TribeBot.Bot.UI;
using TribeBot.Core.Enums;
using TribeBot.Core.Interfaces;

namespace TribeBot.Bot.Handlers
{
    public class RaidButtonHandler : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly IRaidService _raidService;

        public RaidButtonHandler(IRaidService raidService)
        {
            _raidService = raidService;
        }

        // ======================================================
        // YES
        // ======================================================
        [ComponentInteraction(RaidButtonIds.Yes, ignoreGroupNames: true)]
        public async Task RaidYesAsync()
            => await HandleSignupAsync(RaidSignupResponse.Yes);

        // ======================================================
        // MAYBE
        // ======================================================
        [ComponentInteraction(RaidButtonIds.Maybe, ignoreGroupNames: true)]
        public async Task RaidMaybeAsync()
            => await HandleSignupAsync(RaidSignupResponse.Maybe);

        // ======================================================
        // NO
        // ======================================================
        [ComponentInteraction(RaidButtonIds.No, ignoreGroupNames: true)]
        public async Task RaidNoAsync()
            => await HandleSignupAsync(RaidSignupResponse.No);

        // ======================================================
        // CORE HANDLER
        // ======================================================
        private async Task HandleSignupAsync(RaidSignupResponse response)
        {
            await DeferAsync(ephemeral: true);

            if (Context.Interaction is not SocketMessageComponent component)
                return;

            var message = component.Message;
            var user = component.User;

            // ======================================================
            // Resolve raid from messageId
            // ======================================================
            var raid = await _raidService.GetRaidByMessageAsync(message.Id);
            if (raid == null)
            {
                await FollowupAsync(
                    embed: EmbedHelper.Error("This raid signup is no longer active."),
                    ephemeral: true);
                return;
            }

            // ======================================================
            // Register / update signup
            // ======================================================
            await _raidService.RegisterSignupAsync(
                raid.RaidId,
                user.Id,
                response);

            // ======================================================
            // Rebuild embed
            // ======================================================
            var summary = await _raidService.GetSignupSummaryAsync(raid.RaidId);

            var updatedEmbed = RaidEmbedBuilder.BuildWithSignups(
                raid,
                summary);

            await message.ModifyAsync(msg =>
            {
                msg.Embed = updatedEmbed;
            });

            // ======================================================
            // Confirmation
            // ======================================================
            await FollowupAsync(
                embed: EmbedHelper.Success($"Your response has been set to **{response}**."),
                ephemeral: true);
        }
    }
}
