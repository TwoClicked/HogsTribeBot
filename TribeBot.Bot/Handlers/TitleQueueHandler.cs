using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TribeBot.Bot.UI;
using TribeBot.Core.Entities;
using TribeBot.Data.Interfaces;

namespace TribeBot.Bot.Handlers
{
    public class TitleQueueHandler : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly IGoogleSheetsDataStore _data;
        private readonly DiscordSocketClient _client;

        // IDs
        private const ulong PurpleTitleChannelId = 1448989138610028554;
        private const ulong TitleGiverRoleId = 1448989731051540582;
        private const ulong HogsRoleId = 1222668156271591485; 


        public TitleQueueHandler(IGoogleSheetsDataStore data, DiscordSocketClient client)
        {
            _data = data;
            _client = client;
        }

        // ============================================================================
        // /applytitle
        // ============================================================================
        [SlashCommand("applytitle", "Apply for a tribe title: Tycoon or Priest.")]
        public async Task ApplyTitle(string title)
        {

            var user = Context.Guild.GetUser(Context.User.Id);

            if (!user.Roles.Any(r => r.Id == HogsRoleId))
            {
                await RespondAsync(embed: EmbedHelper.Error("You do not have permission."), ephemeral: true);
                return;
            }

            await DeferAsync(ephemeral: true);

            title = title.Trim().ToLower();

            if (title != "tycoon" && title != "priest")
            {
                await FollowupAsync("Invalid title. Choose `tycoon` or `priest`.", ephemeral: true);
                return;
            }

            string discordId = Context.User.Id.ToString();

            var tycoonQueue = await _data.GetTitleQueueAsync("tycoon");
            var priestQueue = await _data.GetTitleQueueAsync("priest");

            var lastTycoon = await _data.GetLastAwardedUserIdAsync("tycoon");
            var lastPriest = await _data.GetLastAwardedUserIdAsync("priest");

            if (lastTycoon == discordId || lastPriest == discordId)
            {
                await FollowupAsync(
                    "You cannot apply for a title while you are the most recent title holder.",
                    ephemeral: true
                );
                return;
            }

            if (tycoonQueue.Any(a => a.DiscordUserId == discordId) ||
                priestQueue.Any(a => a.DiscordUserId == discordId))
            {
                await FollowupAsync("You are already in a title queue.", ephemeral: true);
                return;
            }

            // Load BEFORE adding — this is the fix
            var oldQueue = await _data.GetTitleQueueAsync(title);

            // Add user
            await _data.AddTitleApplicantAsync(title, discordId);

            // Load AFTER adding
            var newQueue = await _data.GetTitleQueueAsync(title);

            bool queueWasEmpty = oldQueue.Count == 0;
            bool queueNowHasOne = newQueue.Count == 1;

            // Notify only when queue was empty and first applicant arrives
            if (queueWasEmpty && queueNowHasOne)
            {
                await NotifyFirstApplicantAsync(title, newQueue[0].DiscordUserId);
            }

            int position = newQueue.FindIndex(a => a.DiscordUserId == discordId) + 1;

            await FollowupAsync(
                $"You have been added to the **{title.ToUpper()}** queue!\nYour current position: **#{position}**",
                ephemeral: true
            );
        }


        private async Task NotifyFirstApplicantAsync(string title, string userId)
        {
            var channel = _client.GetChannel(PurpleTitleChannelId) as IMessageChannel;
            if (channel == null) return;

            var guild = _client.Guilds.FirstOrDefault();
            var user = guild?.GetUser(ulong.Parse(userId));
            string mentionUser = user?.Mention ?? userId;

            var embed = new EmbedBuilder()
                        .WithTitle(title == "tycoon"
                             ? "🎩 New TYCOON Applicant"
                             : "✝️ New PRIEST Applicant")
                        .WithColor(title == "tycoon" ? Color.Blue : Color.Green)
                        .AddField("Applicant", mentionUser)
                        .AddField("Status", "Queue was empty. A title giver may grant the title immediately using `/titlegrant`.")
                        .WithTimestamp(DateTimeOffset.UtcNow)
                        .Build();

            await channel.SendMessageAsync($"<@&{TitleGiverRoleId}> {mentionUser}", embed: embed);
        }

        // ============================================================================
        // /withdrawtitle
        // ============================================================================
        [SlashCommand("withdrawtitle", "Withdraw yourself from any title queue.")]
        public async Task Withdraw()
        {
            await DeferAsync(ephemeral: true); // PREVENT TIMEOUT

            string discordId = Context.User.Id.ToString();

            await _data.RemoveTitleApplicantAsync(discordId);

            await FollowupAsync(
                "You have been removed from the title queue (if you were in one).",
                ephemeral: true
            );
        }


        // ============================================================================
        // /titlequeue
        // ============================================================================
        [SlashCommand("titlequeue", "View the current Tycoon and Priest queues.")]
        public async Task QueueList()
        {
            await DeferAsync(ephemeral: true);

            var tycoon = await _data.GetTitleQueueAsync("tycoon");
            var priest = await _data.GetTitleQueueAsync("priest");

            var embed = new EmbedBuilder()
                .WithTitle("📜 Title Queues")
                .WithColor(Color.Blue);

            embed.AddField("TYCOON",
                tycoon.Count == 0
                    ? "_No applicants_"
                    : string.Join("\n", ListQueueNames(tycoon)));

            embed.AddField("PRIEST",
                priest.Count == 0
                    ? "_No applicants_"
                    : string.Join("\n", ListQueueNames(priest)));

            await FollowupAsync(embed: embed.Build(), ephemeral: true);
        }


        // ============================================================================
        // Helper: Format queue list WITHOUT pinging
        // ============================================================================
        private List<string> ListQueueNames(List<TitleApplicant> list)
        {
            var result = new List<string>();

            foreach (var (app, index) in list.Select((a, i) => (a, i + 1)))
            {
                var user = _client.GetUser(ulong.Parse(app.DiscordUserId));
                string name = user?.Username ?? app.DiscordUserId;

                result.Add($"{index}. {name}");
            }

            return result;
        }

        // ============================================================================
        // Helper: Immediate Announcement when overdue
        // ============================================================================
        private async Task AnnounceImmediateAsync(string title, string userId)
        {
            var channel = _client.GetChannel(PurpleTitleChannelId) as IMessageChannel;
            if (channel == null) return;

            var guild = _client.Guilds.FirstOrDefault();
            var user = guild?.GetUser(ulong.Parse(userId));
            string mentionUser = user?.Mention ?? userId;

            var embed = new EmbedBuilder()
                .WithTitle(title == "tycoon" ? "🎩 TYCOON – Immediate Announcement" : "✝️ PRIEST – Immediate Announcement")
                .WithColor(title == "tycoon" ? Color.Blue : Color.Green)
                .AddField("Next Up", mentionUser)
                .AddField("Queue Status", "Queue was empty and rotation overdue. User announced immediately.")
                .Build();

            await channel.SendMessageAsync($"<@&{TitleGiverRoleId}> {mentionUser}", embed: embed);
        }
    }
}
