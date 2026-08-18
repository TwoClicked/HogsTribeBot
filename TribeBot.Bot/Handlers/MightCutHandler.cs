using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TribeBot.Bot.Modals;
using TribeBot.Bot.UI;

namespace TribeBot.Bot.Handlers
{
    public class MightCutHandler : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly DiscordSocketClient _client;

        private const ulong OfficerRoleId = 1222665812775534592;
        private const ulong OfficerLogChannelId = 1440211043820507217;

        public MightCutHandler(DiscordSocketClient client)
        {
            _client = client;
        }

        // ======================================================
        // /mightcut
        // ======================================================
        [SlashCommand("mightcut", "Send a private might-cut message to a player")]
        public async Task MightCut(
            [Summary("user", "The player who needs to cut might")] SocketGuildUser targetUser)
        {
            if (Context.User is not SocketGuildUser officer ||
                !officer.Roles.Any(r => r.Id == OfficerRoleId))
            {
                await RespondAsync(
                    embed: EmbedHelper.Error("You do not have permission to use this command."),
                    ephemeral: true);
                return;
            }

            var modal = new ModalBuilder()
                .WithTitle("Might Cut Message")
                .WithCustomId("mightcut")
                .AddTextInput("Message to Send", "mightcut_message", TextInputStyle.Paragraph,
                    placeholder: "e.g. You need to cut 34,226,218 might before Sunday's KvK.",
                    required: true)
                .AddTextInput("Target User ID (do not edit)", "target_user_id", TextInputStyle.Short,
                    value: targetUser.Id.ToString(), required: true);

            await RespondWithModalAsync(modal.Build());
        }

        // ======================================================
        // MODAL HANDLER
        // ======================================================
        [ModalInteraction("mightcut", ignoreGroupNames: true)]
        public async Task HandleMightCutModal(MightCutModal modal)
        {
            await DeferAsync(ephemeral: true);

            if (!ulong.TryParse(modal.TargetUserId, out var targetUserId))
            {
                await FollowupAsync(
                    embed: EmbedHelper.Error("Something went wrong reading who this was for — please try the command again."),
                    ephemeral: true);
                return;
            }

            // guild.GetUser() can silently return null for uncached members,
            // so resolve via GetUsersAsync for a reliable lookup.
            var users = await Context.Guild.GetUsersAsync().FlattenAsync();
            var targetUser = users.FirstOrDefault(u => u.Id == targetUserId);

            if (targetUser == null)
            {
                await FollowupAsync(
                    embed: EmbedHelper.Error("Couldn't find that member in the server anymore — they may have left."),
                    ephemeral: true);
                return;
            }

            try
            {
                var dm = await targetUser.CreateDMChannelAsync();
                await dm.SendMessageAsync(modal.Message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MightCut] Failed to DM {targetUser.Username} ({targetUser.Id}): {ex.Message}");
                await FollowupAsync(
                    embed: EmbedHelper.Error($"Couldn't DM {targetUser.Mention} — they likely have DMs disabled for this server."),
                    ephemeral: true);
                return;
            }

            await FollowupAsync(
                embed: EmbedHelper.Success($"Message sent to {targetUser.Mention}."),
                ephemeral: true);

            var officerLog = _client.GetChannel(OfficerLogChannelId) as IMessageChannel;
            if (officerLog != null)
            {
                await officerLog.SendMessageAsync(embed: EmbedHelper.Log("Might Cut Message Sent", new Dictionary<string, string>
                {
                    { "Sent By", Context.User.Username },
                    { "Sent To", $"{targetUser.Username} ({targetUser.Id})" },
                    { "Message", modal.Message }
                }));
            }
        }
    }
}