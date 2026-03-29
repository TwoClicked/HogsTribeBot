using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TribeBot.Bot.UI;
using TribeBot.Core.Entities;

namespace TribeBot.Bot.Handlers
{
    public class SwornHandler : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly DiscordSocketClient _client;

        // ROLE IDs
        private const ulong OfficerRoleId = 1222665812775534592;
        private const ulong HogsEventsRoleId = 1448513656542199880;
        private const ulong HogsRoleId = 1222668156271591485;
        private const ulong EventCoordinatorRoleId = 1284094048260587622;

        // CHANNEL
        private const ulong OfficerLogChannelId = 1440211043820507217;

        public SwornHandler(DiscordSocketClient client)
        {
            _client = client;
        }

        // ============================================================
        // DM SENDER
        // ============================================================
        private async Task<(int sent, List<string> failed)> SendSwornDMsAsync(
            ulong roleId,
            Embed embed)
        {
            var guild = _client.Guilds.FirstOrDefault();
            if (guild == null)
                return (0, new List<string>());

            var members = await guild.GetUsersAsync().FlattenAsync();

            var targets = members
                .Where(u => !u.IsBot && u.RoleIds.Contains(roleId))
                .ToList();

            int sent = 0;
            List<string> failed = new();

            foreach (var member in targets)
            {
                try
                {
                    var dm = await member.CreateDMChannelAsync();
                    await dm.SendMessageAsync(embed: embed);
                    sent++;

                    // Rate limit protection
                    await Task.Delay(1100);
                }
                catch
                {
                    failed.Add($"{member.Username} ({member.Id})");
                }
            }

            return (sent, failed);
        }

        // ============================================================
        // OFFICER LOG
        // ============================================================
        private async Task SendOfficerLogAsync(string title, string triggeredBy, int sent, List<string> failed)
        {
            var channel = _client.GetChannel(OfficerLogChannelId) as IMessageChannel;
            if (channel == null)
                return;

            var embed = new EmbedBuilder()
                .WithTitle(title)
                .WithColor(Color.Orange)
                .AddField("Triggered By", triggeredBy)
                .AddField("DMs Sent", sent, true)
                .AddField("Failed", failed.Count, true)
                .WithTimestamp(DateTimeOffset.UtcNow);

            if (failed.Count > 0)
            {
                string list = string.Join("\n", failed);
                if (list.Length > 1024)
                    list = list.Substring(0, 1000) + "\n... (truncated)";

                embed.AddField("Failed Users", list);
            }

            await channel.SendMessageAsync(embed: embed.Build());
        }

        // ============================================================
        // /hesworn
        // ============================================================
        [SlashCommand("hesworn", "Notify all subscribed users that the next Sworn Vengeance level is unlocked.")]
        public async Task NotifySwornUnlocked()
        {
            var user = Context.Guild.GetUser(Context.User.Id);

            if (!user.Roles.Any(r => r.Id == OfficerRoleId || r.Id == EventCoordinatorRoleId))
            {
                await RespondAsync(embed: EmbedHelper.Error("You do not have permission."), ephemeral: true);
                return;
            }

            // 🔥 Prevent timeout
            await DeferAsync(ephemeral: true);

            var embed = new EmbedBuilder()
                .WithTitle("⚔️ Sworn Vengeance Update")
                .WithDescription(
                    "A new **Sworn Vengeance** boss level has been unlocked!\n\n" +
                    "If you still have to get your attacks in I suggest you log in and attack!\n" +
                    "Keep the momentum going."
                )
                .WithColor(Color.DarkRed)
                .WithFooter("HOGS Event System • 🐗")
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();

            var (sent, failed) = await SendSwornDMsAsync(HogsEventsRoleId, embed);

            await FollowupAsync(
                $"Sworn notification sent!\nSent: **{sent}**, Failed: **{failed.Count}**",
                ephemeral: true
            );

            await SendOfficerLogAsync(
                "⚔️ Sworn Vengeance Notification Summary",
                user.Username,
                sent,
                failed
            );
        }

        // ============================================================
        // /heswornfinal
        // ============================================================
        [SlashCommand("heswornfinal", "Notify the entire HOGS tribe that Sworn Level 15 is unlocked — final race!")]
        public async Task NotifySwornFinal()
        {
            var user = Context.Guild.GetUser(Context.User.Id);

            if (!user.Roles.Any(r => r.Id == OfficerRoleId || r.Id == EventCoordinatorRoleId))
            {
                await RespondAsync(embed: EmbedHelper.Error("You do not have permission."), ephemeral: true);
                return;
            }

            // 🔥 Prevent timeout
            await DeferAsync(ephemeral: true);

            var embed = new EmbedBuilder()
                .WithTitle("🚨 FINAL SWORN VENGEANCE — LEVEL 15 UNLOCKED!")
                .WithDescription(
                    "**ALL HOGS MEMBERS — EMERGENCY CALL!**\n\n" +
                    "Level **15** of Sworn Vengeance has been unlocked.\n" +
                    "This is the FINAL level — every tribe member is needed.\n\n" +
                    "**Log in NOW and use all attacks!**\n" +
                    "The faster we defeat it, the greater the rewards for the entire tribe.\n\n" +
                    "🐗"
                )
                .WithColor(Color.Red)
                .WithFooter("Tribe Priority Alert • 🐗")
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();

            var (sent, failed) = await SendSwornDMsAsync(HogsRoleId, embed);

            await FollowupAsync(
                $"FINAL Sworn Vengeance alert sent!\nSent: **{sent}**, Failed: **{failed.Count}**",
                ephemeral: true
            );

            await SendOfficerLogAsync(
                "🚨 FINAL Sworn Vengeance Level 15 — Tribe Alert Summary",
                user.Username,
                sent,
                failed
            );
        }
    }
}