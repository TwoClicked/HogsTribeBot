using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using TribeBot.Data.Interfaces;
using TribeBot.Core.Entities;

namespace TribeBot.Bot.Handlers
{
    public class TitleGrantHandler : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly IGoogleSheetsDataStore _data;
        private readonly DiscordSocketClient _client;

        private const ulong PurpleTitleChannelId = 1448989138610028554;
        private const ulong TitleGiverRoleId = 1448989731051540582;

        public TitleGrantHandler(IGoogleSheetsDataStore data, DiscordSocketClient client)
        {
            _data = data;
            _client = client;
        }

        // ============================================================================
        // /titlegrant user:@User title:tycoon/priest
        // ============================================================================
        [SlashCommand("titlegrant", "Confirm that a title was granted in-game and advance the rotation.")]
        public async Task GrantTitle(SocketUser user, string title)
        {
            await DeferAsync(ephemeral: true); // PREVENTS TIMEOUT

            title = title.Trim().ToLower();

            if (title != "tycoon" && title != "priest")
            {
                await FollowupAsync("Invalid title. Choose `tycoon` or `priest`.", ephemeral: true);
                return;
            }

            // Ensure the user issuing this command is a title giver
            var guildUser = Context.Guild.GetUser(Context.User.Id);

            if (!guildUser.Roles.Any(r => r.Id == TitleGiverRoleId))
            {
                await FollowupAsync(
                    "You do not have permission to grant titles. Only Title Givers may use this command.",
                    ephemeral: true
                );
                return;
            }

            string grantedUserId = user.Id.ToString();

            // Remove the granted user from queue
            await _data.RemoveTitleApplicantAsync(grantedUserId);

            // Save cooldown
            await _data.SetLastAwardedUserIdAsync(title, grantedUserId);

            // Load queue AFTER removal
            var queue = await _data.GetTitleQueueAsync(title);

            // Select next candidate
            string? nextUserId = queue.FirstOrDefault()?.DiscordUserId;

            // Calculate timers
            DateTime now = DateTime.UtcNow;
            DateTime nextDeadline = now.AddDays(3);

            // Save new timer only if queue not empty
            if (nextUserId != null)
            {
                await _data.SetNextTitleRotationUtcAsync(title, nextDeadline.ToString("o"));
            }
            else
            {
                // No next candidate → clear timer
                await _data.SetNextTitleRotationUtcAsync(title, "");
            }

            // ============================================================================
            // Build announcement embed for Purple Title Channel
            // ============================================================================
            var purpleChannel = _client.GetChannel(PurpleTitleChannelId) as IMessageChannel;

            if (purpleChannel != null && nextUserId != null)
            {
                var nextUser = Context.Guild.GetUser(ulong.Parse(nextUserId));
                string mentionNext = nextUser?.Mention ?? nextUserId;

                List<string> queueList = new();
                foreach (var (app, idx) in queue.Select((a, i) => (a, i + 1)))
                {
                    if (idx == 1) continue;

                    var u = Context.Guild.GetUser(ulong.Parse(app.DiscordUserId));
                    string name = u?.Username ?? app.DiscordUserId;
                    queueList.Add($"{idx}. {name}");
                }

                string queueText = queueList.Count == 0
                    ? "_No further applicants_"
                    : string.Join("\n", queueList);

                var embed = new EmbedBuilder()
                    .WithTitle(title == "tycoon" ? "🎩 TYCOON Title Rotation" : "✝️ PRIEST Title Rotation")
                    .WithColor(title == "tycoon" ? Color.Blue : Color.Green)
                    .AddField("Granted By", Context.User.Mention, inline: true)
                    .AddField("Current Title Holder", user.Mention, inline: true)
                    .AddField("Next Up", mentionNext)
                    .AddField("Queue", queueText)
                    .AddField("Next Rotation Deadline", $"{nextDeadline:yyyy-MM-dd HH:mm} UTC")
                    .AddField("Pre-Announcement", "1 hour before deadline")
                    .WithFooter("Rotation system updates automatically")
                    .WithTimestamp(DateTimeOffset.UtcNow)
                    .Build();

                await purpleChannel.SendMessageAsync(
                    $"<@&{TitleGiverRoleId}> {mentionNext}",
                    embed: embed
                );
            }

            // ============================================================================
            // CONFIRMATION MESSAGE TO TITLE GIVER (NO PINGS)
            // ============================================================================
            string grantedName = user.Username;

            string confirmation = nextUserId == null
                ? $"Title **{title.ToUpper()}** was granted to **{grantedName}**.\nThere are no more applicants in the queue."
                : $"Title **{title.ToUpper()}** was granted to **{grantedName}**.\n" +
                  $"Rotation ends on **{nextDeadline:yyyy-MM-dd HH:mm} UTC**, then the next person will be picked.";

            await FollowupAsync(confirmation, ephemeral: false);
        }
    }
}
