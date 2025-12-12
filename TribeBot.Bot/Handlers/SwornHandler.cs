using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace TribeBot.Bot.Handlers
{
    //Hogs event id : 1448513656542199880 Dev test role id 1439972286877794314 HogsMemberRole 1222668156271591485
    public class SwornHandler : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly DiscordSocketClient _client;

        // ROLE IDs
        private const ulong OfficerRoleId = 1222665812775534592;
        private const ulong HogsEventsRoleId = 1448513656542199880;  // Opt-in event notifications
        private const ulong HogsRoleId = 1222668156271591485;        // Full tribe HOGS role

        // OFFICER LOG CHANNEL
        private const ulong OfficerLogChannelId = 1440211043820507217;

        public SwornHandler(DiscordSocketClient client)
        {
            _client = client;
        }

        // =====================================================================
        // Shared DM sending method
        // =====================================================================
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
            List<string> failed = new List<string>();

            foreach (var member in targets)
            {
                try
                {
                    var dm = await member.CreateDMChannelAsync();
                    await dm.SendMessageAsync(embed: embed);
                    sent++;
                    await Task.Delay(1100); // Rate limit friendly
                }
                catch
                {
                    failed.Add($"{member.Username} ({member.Id})");
                }
            }

            return (sent, failed);
        }

        // =====================================================================
        // Officer log helper
        // =====================================================================
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


        // =====================================================================
        // /hesworn — Notify event subscribers (opt-in role)
        // =====================================================================
        [SlashCommand("hesworn", "Notify all subscribed users that the next Sworn Vengeance level is unlocked.")]
        public async Task NotifySwornUnlocked()
        {
            var officer = Context.Guild.GetUser(Context.User.Id);

            if (!officer.Roles.Any(r => r.Id == OfficerRoleId))
            {
                await RespondAsync("You do not have permission to use this command.", ephemeral: true);
                return;
            }

            var embed = new EmbedBuilder()
                .WithTitle("⚔️ Sworn Vengeance Update")
                .WithDescription(
                    "A new **Sworn Vengeance** boss level has been unlocked!\n\n" +
                    "If you still have to get your attacks in i suggest you log in and attack!\n" +
                    "Keep the momentum going."
                )
                .WithColor(Color.DarkRed)
                .WithFooter("HOGS Event System • 🐗")
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();

            // Send DMs to HogsEvent role (opt-in users)
            var (sent, failed) = await SendSwornDMsAsync(HogsEventsRoleId, embed);

            // Officer receives private confirmation
            await RespondAsync($"Sworn notification sent!\nSent: **{sent}**, Failed: **{failed.Count}**", ephemeral: true);

            // Officer log
            await SendOfficerLogAsync(
                "⚔️ Sworn Vengeance Notification Summary",
                officer.Username,
                sent,
                failed
            );
        }


        // =====================================================================
        // /heswornfinal — Notify ALL HOGS members for Level 15 final race
        // =====================================================================
        [SlashCommand("heswornfinal", "Notify the entire HOGS tribe that Sworn Level 15 is unlocked — final race!")]
        public async Task NotifySwornFinal()
        {
            var officer = Context.Guild.GetUser(Context.User.Id);

            if (!officer.Roles.Any(r => r.Id == OfficerRoleId))
            {
                await RespondAsync("You do not have permission to use this command.", ephemeral: true);
                return;
            }

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

            // Send DMs to ALL HOGS members
            var (sent, failed) = await SendSwornDMsAsync(HogsRoleId, embed);

            // Officer private confirmation
            await RespondAsync($"FINAL Sworn Vengeance alert sent!\nSent: **{sent}**, Failed: **{failed.Count}**", ephemeral: true);

            // Officer log
            await SendOfficerLogAsync(
                "🚨 FINAL Sworn Vengeance Level 15 — Tribe Alert Summary",
                officer.Username,
                sent,
                failed
            );
        }
    }
}
