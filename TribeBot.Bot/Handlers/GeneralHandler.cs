using Discord;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;
using TribeBot.Core.Entities;
using TribeBot.Core.Interfaces;
using TribeBot.Data.Interfaces;
using TribeBot.Services.Services;

namespace TribeBot.Bot.Handlers
{
    public class GeneralHandler
    {
        private readonly DiscordSocketClient _client;
        private readonly IMemberService _memberService;
        private readonly IDonationService _donationService;
        private readonly IFineService _fineService;
        private readonly IGoogleSheetsDataStore _dataStore;
        private readonly IFarmService _farmService;
        private readonly IFarmTribeService _farmTribeService;
        private readonly IFarmTribeAssignmentService _assignmentService;


        private const ulong OfficerRoleId = 1222665812775534592;

        public GeneralHandler(
            DiscordSocketClient client,
            IMemberService memberService,
            IDonationService donationService,
            IFineService fineService,
            IGoogleSheetsDataStore dataStore,
            IFarmService farmService,
            IFarmTribeService farmTribeService,
            IFarmTribeAssignmentService assignmentService)
        {
            _client = client;
            _memberService = memberService;
            _donationService = donationService;
            _fineService = fineService;
            _dataStore = dataStore;
            _farmService = farmService;
            _farmTribeService = farmTribeService;
            _assignmentService = assignmentService;
        }

        // ======================================================================
        // EMBEDS (local)
        // ======================================================================
        private Embed BuildEmbed(string title, string desc, Color color)
        {
            return new EmbedBuilder()
                .WithTitle(title)
                .WithDescription(desc)
                .WithColor(color)
                .WithFooter($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC")
                .Build();
        }

        // MESSAGE-based helpers ==============================
        private Task SendSuccess(SocketMessage msg, string text)
            => msg.Channel.SendMessageAsync(embed: BuildEmbed("🟢 Success", text, Color.Green));

        private Task SendError(SocketMessage msg, string text)
            => msg.Channel.SendMessageAsync(embed: BuildEmbed("❌ Error", text, Color.Red));

        private Task SendWarning(SocketMessage msg, string text)
            => msg.Channel.SendMessageAsync(embed: BuildEmbed("⚠️ Warning", text, Color.Orange));

        private Task SendInfo(SocketMessage msg, string title, string text)
            => msg.Channel.SendMessageAsync(embed: BuildEmbed($"🛡️ {title}", text, Color.Blue));

        // CHANNEL-based helpers (required to fix your error) ==================
        private Task SendSuccess(IMessageChannel ch, string text)
            => ch.SendMessageAsync(embed: BuildEmbed("🟢 Success", text, Color.Green));

        private Task SendError(IMessageChannel ch, string text)
            => ch.SendMessageAsync(embed: BuildEmbed("❌ Error", text, Color.Red));

        private Task SendWarning(IMessageChannel ch, string text)
            => ch.SendMessageAsync(embed: BuildEmbed("⚠️ Warning", text, Color.Orange));

        private Task SendInfo(IMessageChannel ch, string title, string text)
            => ch.SendMessageAsync(embed: BuildEmbed($"🛡️ {title}", text, Color.Blue));

        // ======================================================================
        // ROOT HANDLER
        // ======================================================================
        public async Task<bool> TryHandleAsync(SocketMessage message)
        {
            if (message.Author.IsBot) return false;

            string content = message.Content.Trim().ToLower();

            if (content == "!myinfo")
            {
                await MyInfo(message);
                return true;
            }

            if (content == "!listmembers")
            {
                await ListMembers(message);
                return true;
            }

            if (content.StartsWith("!viewinfo"))
            {
                await ViewInfo(message);
                return true;
            }

            if (content.StartsWith("!listnonregistered"))
            {
                await ViewNonRegisteredMembers(message);
                return true;
            }

            if (content.StartsWith("!removemember"))
            {
                await RemoveMember(message);
                return true;
            }

            if (content.StartsWith("!registerreminder"))
            {
                await SendRegisterReminders(message);
                return true;
            }

            return false;
        }

        // ======================================================================
        // !registerreminder (OFFICERS)
        // ======================================================================
        private async Task SendRegisterReminders(SocketMessage message)
        {
            if (message.Content != "!registerreminder") return;

            if (message.Channel is not SocketGuildChannel)
            {
                await SendError(message, "This command can only be used inside the server.");
                return;
            }

            var caller = message.Author as SocketGuildUser;

            if (!caller.Roles.Any(r => r.Id == OfficerRoleId))
            {
                await SendError(message, $"{caller.Mention} you do not have permission.");
                return;
            }

            await SendInfo(message, "Registry Reminder", "📨 Sending reminders in the background…");

            _ = Task.Run(async () =>
            {
                await SendRegistrationReminderManualAsync(message.Channel);
            });
        }

        private async Task SendRegistrationReminderManualAsync(IMessageChannel origin)
        {
            try
            {
                var guild = _client.GetGuild(1109193500664287336);

                ulong hogsRoleId = 1222668156271591485;
                var hogsRole = guild.GetRole(hogsRoleId);

                if (hogsRole == null)
                {
                    await SendError(origin, "HOGS role not found.");
                    return;
                }

                var registered = await _memberService.GetAllMembersAsync();
                var registeredIds = registered.Select(m => m.DiscordUserId).ToHashSet();

                var unregistered = hogsRole.Members
                    .Where(u => !registeredIds.Contains(u.Id.ToString()))
                    .ToList();

                if (unregistered.Count == 0)
                {
                    await SendSuccess(origin, "All HOGS members are already registered!");
                    return;
                }

                int sent = 0;
                var failed = new System.Collections.Generic.List<ulong>();

                foreach (var user in unregistered)
                {
                    try
                    {
                        var dm = await user.CreateDMChannelAsync();
                        await dm.SendMessageAsync(
                            $"👋 Hello **{user.Username}**, you still need to register with the Tribe Bot.\n" +
                            $"Please type `!register` here to complete registration.");
                        sent++;
                        await Task.Delay(1200);
                    }
                    catch
                    {
                        failed.Add(user.Id);
                        await Task.Delay(2000);
                    }
                }

                string summary =
                    $"📨 **Registration Reminder Summary**\n" +
                    $"• Unregistered: **{unregistered.Count}**\n" +
                    $"• DMs sent: **{sent}**\n" +
                    $"• Failed: **{failed.Count}**";

                await SendInfo(origin, "Reminder Report", summary);
            }
            catch (Exception ex)
            {
                await SendError(origin, $"Error sending reminders: {ex.Message}");
            }
        }

        // ======================================================================
        // !removemember (OFFICER)
        // ======================================================================
        private async Task RemoveMember(SocketMessage message)
        {
            if (!message.Content.StartsWith("!removemember")) return;

            if (message.Channel is not SocketGuildChannel)
            {
                await SendError(message, "This command can only be used inside the server.");
                return;
            }

            var caller = message.Author as SocketGuildUser;

            if (!caller.Roles.Any(r => r.Id == OfficerRoleId))
            {
                await SendError(message, $"{caller.Mention} you do not have permission to use this command.");
                return;
            }

            string args = message.Content.Substring("!removemember".Length).Trim();

            if (string.IsNullOrWhiteSpace(args))
            {
                await SendWarning(message, "Usage: `!removemember @user` or `!removemember ingameName`");
                return;
            }

            Member? memberToRemove = null;

            if (message.MentionedUsers.Count > 0)
            {
                var target = message.MentionedUsers.First();
                memberToRemove = await _memberService.GetMemberByDiscordIdAsync(target.Id.ToString());
            }
            else
            {
                var all = await _memberService.GetAllMembersAsync();
                memberToRemove = all.FirstOrDefault(m => m.IngameName.Equals(args, StringComparison.OrdinalIgnoreCase));
            }

            if (memberToRemove == null)
            {
                await SendError(message, $"No registered member found matching **{args}**.");
                return;
            }

            bool success = await _dataStore.RemoveMemberByDiscordIdAsync(memberToRemove.DiscordUserId);

            if (success)
                await SendSuccess(message, $"Removed **{memberToRemove.IngameName}** from the member list.");
            else
                await SendError(message, $"Removal failed — member not found in the sheets.");
        }

        // ======================================================================
        // !listnonregistered
        // ======================================================================
        private async Task ViewNonRegisteredMembers(SocketMessage message)
        {
            if (message.Content != "!listnonregistered") return;

            if (message.Channel is not SocketGuildChannel sgc)
            {
                await SendError(message, "This command can only be used inside the server.");
                return;
            }

            var guild = sgc.Guild;
            var registered = await _memberService.GetAllMembersAsync();
            var registeredIds = registered.Select(m => m.DiscordUserId).ToHashSet();

            ulong hogsRoleId = 1222668156271591485;
            var hogsRole = guild.GetRole(hogsRoleId);

            var nonregistered = hogsRole.Members
                .Where(u => !registeredIds.Contains(u.Id.ToString()))
                .ToList();

            if (nonregistered.Count == 0)
            {
                await SendSuccess(message, "🎉 Everyone with the HOGS role is registered!");
                return;
            }

            string list = string.Join("\n", nonregistered.Select(n => $"• **{n.DisplayName}**"));

            await SendInfo(message, "Non-Registered Members", list);
        }

        // ======================================================================
        // !myinfo
        // ======================================================================
        private async Task MyInfo(SocketMessage message)
        {
            string id = message.Author.Id.ToString();
            var member = await _memberService.GetMemberByDiscordIdAsync(id);

            if (member == null)
            {
                await SendError(message, "You are not registered. Use `!register`.");
                return;
            }

            // Farm tribe info
            string farmTribeDisplay = "Unassigned";

            var assignment = await _assignmentService.GetAssignmentForUserAsync(id);
            if (assignment != null)
            {
                var tribe = await _farmTribeService.GetFarmTribeByIdAsync(assignment.FarmTribeId);
                if (tribe != null)
                    farmTribeDisplay = tribe.FarmTribeName;
            }

            // Farm count
            var farms = await _farmService.GetFarmsForUserAsync(id);
            int farmCount = farms.Count;

            var donations = await _donationService.GetTotalForUserThisWeekAsync(id);
            string donationStatus =
                member.IsExempt ? "🟦 EXEMPT" :
                donations > 0 ? "✅ PAID" :
                "❌ UNPAID";

            var embed = new EmbedBuilder()
                .WithTitle("📘 Your Profile")
                .WithColor(Color.Blue)
                .AddField("👤 Basic Info",
                    $"**Name:** {member.IngameName}\n" +
                    $"**ID:** {member.IngameId}\n" +
                    $"**Farm Tribe:** {farmTribeDisplay}\n" +
                    $"**Registered Farms:** {farmCount}",
                    inline: false)
                .AddField("⚔ Stats",
                    $"**Might:** {member.Might:N0}\n" +
                    $"**Kills:** {member.KillPoints:N0}\n" +
                    $"**Collector Level:** {member.CollectorLevel}\n" +
                    $"**Reign Points:** {member.ReignPoints}",
                    inline: false)
                .AddField("🏦 Donation Status", donationStatus, inline: true)
                .AddField("🛡️ Exempt", member.IsExempt ? "Yes" : "No", inline: true)
                .WithFooter($"Last Updated: {member.LastUpdatedUTC:yyyy-MM-dd HH:mm} UTC")
                .Build();

            await message.Channel.SendMessageAsync(embed: embed);
        }


        // ======================================================================
        // !listmembers
        // ======================================================================
        private async Task ListMembers(SocketMessage message)
        {
            var members = await _memberService.GetAllMembersAsync();

            if (members.Count == 0)
            {
                await SendError(message, "No members found.");
                return;
            }

            string msg =
                "📜 **Member List (A–Z)**\n\n" +
                string.Join("\n",
                    members.OrderBy(m => m.IngameName)
                           .Select(m => $"• **{m.IngameName}** — `{m.IngameId}`"));

            await SendLong(message.Channel, msg);
        }

        // ======================================================================
        // !viewinfo (OFFICER)
        // ======================================================================
        private async Task ViewInfo(SocketMessage message)
        {
            if (!IsOfficer(message)) return;

            if (message.MentionedUsers.Count == 0)
            {
                await SendWarning(message, "Usage: `!viewinfo @user`");
                return;
            }

            var target = message.MentionedUsers.First();
            string id = target.Id.ToString();

            var member = await _memberService.GetMemberByDiscordIdAsync(id);

            // Farm tribe info
            string farmTribeDisplay = "Unassigned";

            var assignment = await _assignmentService.GetAssignmentForUserAsync(id);
            if (assignment != null)
            {
                var tribe = await _farmTribeService.GetFarmTribeByIdAsync(assignment.FarmTribeId);
                if (tribe != null)
                    farmTribeDisplay = tribe.FarmTribeName;
            }

            // Farm count
            var farms = await _farmService.GetFarmsForUserAsync(id);
            int farmCount = farms.Count;

            if (member == null)
            {
                await SendError(message, $"{target.Username} is not registered.");
                return;
            }

            var donations = await _donationService.GetTotalForUserThisWeekAsync(id);
            string donationStatus =
                member.IsExempt ? "🟦 EXEMPT" :
                donations > 0 ? "✅ PAID" : "❌ UNPAID";

            var fines = await _fineService.GetFinesForUserAsync(id);

            // Build fine lists
            string unpaid = fines
                .Where(f => !f.IsPaid)
                .Select(f => $"• {f.Amount:N0} — `{f.FineType}` — **{f.FineId}** ({f.PaidAmount}/{f.Amount})")
                .DefaultIfEmpty("• None")
                .Aggregate((a, b) => a + "\n" + b);

            string paid = fines
                .Where(f => f.IsPaid)
                .Select(f => $"• {f.Amount:N0} — `{f.FineType}` — **{f.FineId}** — PAID")
                .DefaultIfEmpty("• None")
                .Aggregate((a, b) => a + "\n" + b);

            // Build embed
            var embed = new EmbedBuilder()
                .WithTitle($"📘 Profile for {target.Username}")
                .WithColor(Color.Blue)
                .AddField("👤 Basic Info",
                    $"**Name:** {member.IngameName}\n" +
                    $"**ID:** {member.IngameId}\n" +
                    $"**Farm Tribe:** {farmTribeDisplay}\n" +
                    $"**Registered Farms:** {farmCount}\n" +
                    $"**Exempt:** {(member.IsExempt ? "Yes" : "No")}")
                
                .AddField("⚔ Stats",
                    $"**Might:** {member.Might:N0}\n" +
                    $"**Kills:** {member.KillPoints:N0}\n" +
                    $"**Collector Level:** {member.CollectorLevel}\n" +
                    $"**Reign Points:** {member.ReignPoints}")
                .AddField("🏦 Donation Status", donationStatus, true)
                .AddField("💀 Unpaid Fines", unpaid.Length > 1024 ? unpaid[..1024] : unpaid)
                .AddField("🟩 Paid Fines", paid.Length > 1024 ? paid[..1024] : paid)
                .WithFooter($"Last Updated: {member.LastUpdatedUTC:yyyy-MM-dd HH:mm} UTC")
                .Build();

            await message.Channel.SendMessageAsync(embed: embed);
        }


        // ======================================================================
        // HELPERS
        // ======================================================================
        private bool IsOfficer(SocketMessage message)
        {
            if (message.Channel is not SocketGuildChannel sgc)
            {
                _ = SendError(message, "This command must be used inside the server.");
                return false;
            }

            var user = sgc.GetUser(message.Author.Id);

            if (user == null || !user.Roles.Any(r => r.Id == OfficerRoleId))
            {
                _ = SendError(message, $"{message.Author.Mention}, you do not have permission.");
                return false;
            }

            return true;
        }

        private async Task SendLong(IMessageChannel ch, string text)
        {
            const int limit = 1990;

            if (text.Length <= limit)
            {
                await ch.SendMessageAsync(text);
                return;
            }

            var lines = text.Split('\n');
            string buffer = "";

            foreach (string line in lines)
            {
                if (buffer.Length + line.Length > limit)
                {
                    await ch.SendMessageAsync(buffer);
                    buffer = "";
                }
                buffer += line + "\n";
            }

            if (buffer.Length > 0)
                await ch.SendMessageAsync(buffer);
        }
    }
}
