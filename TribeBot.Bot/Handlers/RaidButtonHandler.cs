using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using System;
using System.Threading.Tasks;
using TribeBot.Bot.UI;
using TribeBot.Core.DTOS;
using TribeBot.Core.Entities;
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
                msg.Components = RaidEmbedBuilder.BuildRaidComponents();
            });


            // ======================================================
            // Confirmation
            // ======================================================
            await FollowupAsync(
                embed: EmbedHelper.Success($"Your response has been set to **{response}**."),
                ephemeral: true);


        }

        [ComponentInteraction(RaidButtonIds.ShowRoster, ignoreGroupNames: true)]
        public async Task ShowRosterAsync()
        {
            await DeferAsync(ephemeral: true);

            if (Context.Interaction is not SocketMessageComponent component)
                return;

            var message = component.Message;

            var raid = await _raidService.GetRaidByMessageAsync(message.Id);
            if (raid == null)
            {
                await FollowupAsync(
                    embed: EmbedHelper.Error("This raid no longer exists."),
                    ephemeral: true);
                return;
            }

            var summary = await _raidService.GetSignupSummaryAsync(raid.RaidId);

            var messages = BuildFullRosterMessages(raid, summary);

            // Send first chunk
            await FollowupAsync(
                text: messages.First(),
                ephemeral: true);

            // Send remaining chunks
            foreach (var msg in messages.Skip(1))
            {
                await FollowupAsync(
                    text: msg,
                    ephemeral: true);
            }
        }


        // HELPER 

        private static IEnumerable<string> BuildFullRosterMessages(
              Raid raid,
              RaidSignupSummary summary)
        {
            var lines = new List<string>
               {
                   raid.RaidType == RaidType.Gate
                       ? "**🚪 Gate Raid Roster**"
                       : "**⚔️ Killing Field Raid Roster**"
               };

            void AddSection(string title, IEnumerable<ulong> users)
            {
                lines.Add($"\n**{title} ({users.Count()})**");

                if (!users.Any())
                {
                    lines.Add("_No signups_");
                    return;
                }

                lines.AddRange(users.Select(id => $"• <@{id}>"));
            }

            AddSection("✅ YES", summary.Yes);
            AddSection("❔ MAYBE", summary.Maybe);
            AddSection("❌ NO", summary.No);

            const int MaxLength = 1900;
            var chunk = "";

            foreach (var line in lines)
            {
                if (chunk.Length + line.Length + 1 > MaxLength)
                {
                    yield return chunk;
                    chunk = "";
                }

                chunk += line + "\n";
            }

            if (!string.IsNullOrWhiteSpace(chunk))
                yield return chunk;
        }


    }
}
