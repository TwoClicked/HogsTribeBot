using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Google.Apis.Drive.v3.Data;
using System;
using System.Linq;
using System.Threading.Tasks;
using TribeBot.Bot.UI;
using TribeBot.Core.Entities;
using TribeBot.Data.Interfaces;

namespace TribeBot.Bot.Handlers
{
    public class TitleStatusHandler : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly IGoogleSheetsDataStore _data;
        private readonly DiscordSocketClient _client;
        private const ulong OfficerRoleId = 1222665812775534592;
        private const ulong HogsRoleId = 1222668156271591485;

        public TitleStatusHandler(IGoogleSheetsDataStore data, DiscordSocketClient client)
        {
            _data = data;
            _client = client;
        }

        // ============================================================================
        // /currenttitles — Show holders, queue, next rotation, live countdowns
        // ============================================================================
        [SlashCommand("currenttitles", "View the current title holders, queue, and rotation countdown.")]
        public async Task CurrentTitles()
        {

            await DeferAsync(ephemeral: false);

            var user = Context.User as SocketGuildUser;

            if (!user.Roles.Any(r => r.Id == OfficerRoleId))
            {
                await RespondAsync(embed: EmbedHelper.Error("You do not have permission."), ephemeral: true);
                return;
            }

            var guild = Context.Guild;

            // Fetch holders
            string? lastTycoonId = await _data.GetLastAwardedUserIdAsync("tycoon");
            string? lastPriestId = await _data.GetLastAwardedUserIdAsync("priest");

            // Fetch rotation times
            string? tycoonRotation = await _data.GetNextTitleRotationUtcAsync("tycoon");
            string? priestRotation = await _data.GetNextTitleRotationUtcAsync("priest");

            // Fetch queues
            var tycoonQueue = await _data.GetTitleQueueAsync("tycoon");
            var priestQueue = await _data.GetTitleQueueAsync("priest");

            // Resolve users
            string tycoonHolder =
                lastTycoonId == null ? "_None_" :
                $" `{guild.GetUser(ulong.Parse(lastTycoonId))?.DisplayName}`" ?? lastTycoonId;

            string priestHolder =
                lastPriestId == null ? "_None_" :
                $" `{guild.GetUser(ulong.Parse(lastPriestId))?.DisplayName}`" ?? lastPriestId;

            string tycoonNext =
                tycoonQueue.FirstOrDefault()?.DiscordUserId is string idT
                ? $" `{guild.GetUser(ulong.Parse(idT))?.DisplayName}`" ?? idT
                : "_None_";

            string priestNext =
                priestQueue.FirstOrDefault()?.DiscordUserId is string idP
                ? $" `{guild.GetUser(ulong.Parse(idP))?.DisplayName}`" ?? idP
                : "_None_";


            // Build countdown text for TYCOON
            string tycoonCountdown = "_Not scheduled_";
            string tycoonPre = "_Not scheduled_";

            if (!string.IsNullOrWhiteSpace(tycoonRotation) &&
                DateTime.TryParse(tycoonRotation, out DateTime tyDeadline))
            {
                long deadlineUnix = ((DateTimeOffset)tyDeadline).ToUnixTimeSeconds();
                long preUnix = ((DateTimeOffset)tyDeadline.AddHours(-1)).ToUnixTimeSeconds();

                tycoonCountdown = $"<t:{deadlineUnix}:F> (**<t:{deadlineUnix}:R>**)";
                tycoonPre = $"<t:{preUnix}:F> (**<t:{preUnix}:R>**)";
            }

            // Build countdown text for PRIEST
            string priestCountdown = "_Not scheduled_";
            string priestPre = "_Not scheduled_";

            if (!string.IsNullOrWhiteSpace(priestRotation) &&
                DateTime.TryParse(priestRotation, out DateTime prDeadline))
            {
                long deadlineUnix = ((DateTimeOffset)prDeadline).ToUnixTimeSeconds();
                long preUnix = ((DateTimeOffset)prDeadline.AddHours(-1)).ToUnixTimeSeconds();

                priestCountdown = $"<t:{deadlineUnix}:F> (**<t:{deadlineUnix}:R>**)";
                priestPre = $"<t:{preUnix}:F> (**<t:{preUnix}:R>**)";
            }


            // Build final embed
            var embed = new EmbedBuilder()
                .WithTitle("🏅 Current Title Holders")
                .WithColor(Color.Gold)

                .AddField("🎩 TYCOON",
                    $"**Current Holder:** {tycoonHolder}\n" +
                    $"**Next In Queue:** {tycoonNext}\n" +
                    $"**Rotation Ends:** {tycoonCountdown}\n" +
                    $"**Pre-Announcement:** {tycoonPre}")

                .AddField("✝️ PRIEST",
                    $"**Current Holder:** {priestHolder}\n" +
                    $"**Next In Queue:** {priestNext}\n" +
                    $"**Rotation Ends:** {priestCountdown}\n" +
                    $"**Pre-Announcement:** {priestPre}")

                .WithFooter("Title rotation updates automatically")
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();

            await FollowupAsync(embed: embed);
        }
    }
}
