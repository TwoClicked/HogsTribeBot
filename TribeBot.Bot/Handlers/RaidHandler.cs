using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using TribeBot.Bot.Modals;
using TribeBot.Bot.UI;
using TribeBot.Core.Enums;
using TribeBot.Core.Interfaces;

namespace TribeBot.Bot.Handlers
{
    [Group("raid", "Raid signup management")]
    public class RaidHandler : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly IRaidService _raidService;

        // Officer role
        private const ulong OfficerRoleId = 1222665812775534592;

        public RaidHandler(IRaidService raidService)
        {
            _raidService = raidService;
        }

        // ======================================================
        // /raid create
        // ======================================================
        [SlashCommand("create", "Create a raid signup (Gate or Killing Field)")]
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

        // ======================================================
        // MODAL SUBMIT
        // ======================================================
        [ModalInteraction("raid_create", ignoreGroupNames: true)]
        public async Task HandleCreateRaidAsync(AddRaidModal modal)
        {
            await DeferAsync(ephemeral: true);

            // ======================================================
            // Parse UTC datetime
            // ======================================================
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

            // ======================================================
            // Validate raid type
            // ======================================================
            var raidTypeInput = modal.RaidType.Trim().ToLower();
            if (raidTypeInput != "gate" && raidTypeInput != "killingfield")
            {
                await FollowupAsync(
                    embed: EmbedHelper.Error(
                        "Raid type must be **Gate** or **KillingField**."),
                    ephemeral: true);
                return;
            }

            var raidType = raidTypeInput == "gate"
                ? RaidType.Gate
                : RaidType.KillingField;

            // ======================================================
            // Build initial embed + buttons
            // ======================================================
            var embed = RaidEmbedBuilder.BuildInitial(
                modal.KvKId.Trim(),
                raidType,
                startUtc
            );

            var components = new ComponentBuilder()
                .WithButton("YES", RaidButtonIds.Yes, ButtonStyle.Success)
                .WithButton("MAYBE", RaidButtonIds.Maybe, ButtonStyle.Secondary)
                .WithButton("NO", RaidButtonIds.No, ButtonStyle.Danger)
                .Build();

            // ======================================================
            // Send message FIRST (we need messageId)
            // ======================================================
            var channel = Context.Channel;
            var message = await channel.SendMessageAsync(
                embed: embed,
                components: components
            );

            // ======================================================
            // Persist raid (now we have channelId + messageId)
            // ======================================================
            await _raidService.CreateRaidAsync(
                raidType,
                startUtc,
                channel.Id,
                message.Id
            );

            // ======================================================
            // Confirmation
            // ======================================================
            await FollowupAsync(
                embed: EmbedHelper.Success(
                    $"Raid signup created.\n\n" +
                    $"**KvK ID:** `{modal.KvKId}`\n" +
                    $"**Type:** `{raidType}`\n" +
                    $"**Start:** `{startUtc:yyyy-MM-dd HH:mm} UTC`"),
                ephemeral: true);
        }


        // ======================================================
        // BUTTON HANDLERS
        // ======================================================
        [ComponentInteraction(RaidButtonIds.Yes)]
        public async Task HandleRaidYesAsync()
            => await HandleRaidSignupAsync(RaidSignupResponse.Yes);

        [ComponentInteraction(RaidButtonIds.Maybe)]
        public async Task HandleRaidMaybeAsync()
            => await HandleRaidSignupAsync(RaidSignupResponse.Maybe);

        [ComponentInteraction(RaidButtonIds.No)]
        public async Task HandleRaidNoAsync()
            => await HandleRaidSignupAsync(RaidSignupResponse.No);


        private async Task HandleRaidSignupAsync(RaidSignupResponse response)
        {
            await DeferAsync(ephemeral: true);

            // ======================================================
            // Ensure this is a button interaction
            // ======================================================
            if (Context.Interaction is not SocketMessageComponent component)
            {
                await FollowupAsync(
                    embed: EmbedHelper.Error("Invalid interaction type."),
                    ephemeral: true);
                return;
            }

            var message = component.Message;
            var messageId = message.Id;

            // ======================================================
            // Identify raid via messageId
            // ======================================================
            var raid = await _raidService.GetRaidByMessageAsync(messageId);
            if (raid == null)
            {
                await FollowupAsync(
                    embed: EmbedHelper.Error("This raid signup no longer exists."),
                    ephemeral: true);
                return;
            }

            // ======================================================
            // Register / update signup
            // ======================================================
            await _raidService.RegisterSignupAsync(
                raid.RaidId,
                Context.User.Id,
                response);

            // ======================================================
            // Fetch updated summary
            // ======================================================
            var summary = await _raidService.GetSignupSummaryAsync(raid.RaidId);

            // ======================================================
            // Rebuild embed
            // ======================================================
            var updatedEmbed = RaidEmbedBuilder.BuildWithSignups(
                raid,
                summary);

            // ======================================================
            // Update original message
            // ======================================================
            await message.ModifyAsync(msg =>
            {
                msg.Embed = updatedEmbed;
            });

            await FollowupAsync(
                embed: EmbedHelper.Success($"You are marked as **{response}**."),
                ephemeral: true);
        }
    }
}
