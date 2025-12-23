using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using TribeBot.Data.Interfaces;
using TribeBot.Core.Entities;
using TribeBot.Bot.Services; // IMPORTANT to access SchedulerService.PreAnnounced

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

        [SlashCommand("titlegrant", "Confirm that a title was granted in-game and advance the rotation.")]
        public async Task GrantTitle(SocketUser user, string title)
        {
            await DeferAsync(ephemeral: true);

            title = title.ToLower().Trim();
            if (title != "tycoon" && title != "priest")
            {
                await FollowupAsync("Invalid title. Choose `tycoon` or `priest`.", ephemeral: true);
                return;
            }

            var giver = Context.Guild.GetUser(Context.User.Id);
            if (!giver.Roles.Any(r => r.Id == TitleGiverRoleId))
            {
                await FollowupAsync("Only Title Givers may grant titles.", ephemeral: true);
                return;
            }

            string grantedUserId = user.Id.ToString();

            // Load queue BEFORE removal
            var queueBefore = await _data.GetTitleQueueAsync(title);

            // Next person in line BEFORE modifying queue
            string? nextUserId =
                queueBefore.Count > 1 ? queueBefore[1].DiscordUserId : null;

            // Always set new 3-day rotation timer
            DateTime nextDeadline = DateTime.UtcNow.AddDays(3);
            await _data.SetNextTitleRotationUtcAsync(title, nextDeadline.ToString("o"));

            // Remove the awarded person from the queue
            await _data.RemoveTitleApplicantAsync(grantedUserId);

            // Save cooldown
            await _data.SetLastAwardedUserIdAsync(title, grantedUserId);

            // RESET pre-announcement flag
            SchedulerService.PreAnnounced[title] = false;

            // ---------------------------
            // SEND EMBED TO PUBLIC CHANNEL
            // ---------------------------
            var channel = _client.GetChannel(PurpleTitleChannelId) as IMessageChannel;
            if (channel != null)
            {
                if (nextUserId != null)
                {
                    var nextUser = Context.Guild.GetUser(ulong.Parse(nextUserId));
                    string nextDisplayName = nextUser?.DisplayName ?? nextUserId;

                    var queueText =
                        queueBefore.Count <= 2
                        ? "_No further applicants_"
                        : string.Join("\n",
                            queueBefore.Skip(2).Select((a, i) =>
                            {
                                var u = Context.Guild.GetUser(ulong.Parse(a.DiscordUserId));
                                return $"{i + 3}. {(u?.Username ?? a.DiscordUserId)}";
                            }));

                    var embed = new EmbedBuilder()
                        .WithTitle(title == "tycoon" ? "🎩 TYCOON Title Rotation" : "✝️ PRIEST Title Rotation")
                        .WithColor(title == "tycoon" ? Color.Blue : Color.Green)
                        .AddField("Granted By", giver.Mention, inline: true)
                        .AddField("New Title Holder", user.Mention, inline: true)
                        .AddField("Next Up", nextDisplayName)
                        .AddField("Queue", queueText)
                        .AddField("Next Rotation Deadline", $"{nextDeadline:yyyy-MM-dd HH:mm} UTC")
                        .AddField("Pre-Announcement", "1 hour before deadline")
                        .WithFooter("Rotation system updates automatically")
                        .WithTimestamp(DateTimeOffset.UtcNow)
                        .Build();

                    await channel.SendMessageAsync($"<@&{TitleGiverRoleId}>", embed: embed);
                }
                else
                {
                    var embed = new EmbedBuilder()
                        .WithTitle(title == "tycoon" ? "🎩 TYCOON Title Granted" : "✝️ PRIEST Title Granted")
                        .WithColor(Color.DarkGrey)
                        .AddField("Granted By", giver.Mention, inline: true)
                        .AddField("New Title Holder", user.Mention, inline: true)
                        .AddField("Queue", "_No further applicants_")
                        .AddField("Next Rotation Deadline", $"{nextDeadline:yyyy-MM-dd HH:mm} UTC")
                        .WithFooter("Timer still set — scheduler will announce if queue remains empty")
                        .WithTimestamp(DateTimeOffset.UtcNow)
                        .Build();

                    await channel.SendMessageAsync($"<@&{TitleGiverRoleId}>", embed: embed);
                }
            }

            await FollowupAsync(
                $"Title **{title.ToUpper()}** was granted to **{user.Username}**.\nNext rotation deadline: **{nextDeadline:yyyy-MM-dd HH:mm} UTC**.",
                ephemeral: false
            );
        }
    }
}
