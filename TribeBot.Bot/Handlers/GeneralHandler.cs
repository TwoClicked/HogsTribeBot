using Discord;
using Discord.WebSocket;
using Google.Apis.Util.Store;
using System.Linq;
using System.Threading.Tasks;
using TribeBot.Core.Entities;
using TribeBot.Core.Interfaces;
using TribeBot.Data.Interfaces;

namespace TribeBot.Bot.Handlers
{
    public class GeneralHandler
    {
        private readonly DiscordSocketClient _client;
        private readonly IMemberService _memberService;
        private readonly IDonationService _donationService;
        private readonly IFineService _fineService;
        private readonly IGoogleSheetsDataStore _dataStore;

        private const ulong OfficerRoleId = 1222665812775534592;


        public GeneralHandler(
            DiscordSocketClient client,
            IMemberService memberService,
            IDonationService donationService,
            IFineService fineService,
            IGoogleSheetsDataStore dataStore)
        {
            _client = client;
            _memberService = memberService;
            _donationService = donationService;
            _fineService = fineService;
            _dataStore = dataStore;
        }



        public async Task<bool> TryHandleAsync(SocketMessage message)
        {
            if (message.Author.IsBot)
                return false;

            string content = message.Content.Trim();

            if (content.Equals("!myinfo", System.StringComparison.OrdinalIgnoreCase))
            {
                await MyInfo(message);
                return true;
            }

            if (content.Equals("!listmembers", System.StringComparison.OrdinalIgnoreCase))
            {
                await ListMembers(message);
                return true;
            }

            if (content.StartsWith("!viewinfo", System.StringComparison.OrdinalIgnoreCase))
            {
                await ViewInfo(message);
                return true;
            }

            if (content.StartsWith("!listnonregistered", System.StringComparison.OrdinalIgnoreCase))
            {
                await ViewNonRegisteredMembers(message);
                return true;
            }

            if (content.StartsWith("!removemember", System.StringComparison.OrdinalIgnoreCase))
            {
                await RemoveMember(message);
                return true;
            }

            if (content.StartsWith("!registerreminder", System.StringComparison.OrdinalIgnoreCase))
            {
                await SendRegisterReminders(message);
                return true;
            }
            return false;
        }

        private async Task SendRegisterReminders(SocketMessage message)
        {
            // ============================
            // !registerreminder (manual)
            // ============================

            if (message.Content.Equals("!registerreminder", StringComparison.OrdinalIgnoreCase))
            {
                if (message.Channel is not SocketGuildChannel)
                {
                    await message.Channel.SendMessageAsync("This command can only be used in the server.");
                    return;
                }

                var caller = message.Author as SocketGuildUser;
                ulong officerRoleId = 1222665812775534592;

                if (!caller.Roles.Any(r => r.Id == officerRoleId))
                {
                    await message.Channel.SendMessageAsync($"{caller.Mention} you do not have permission.");
                    return;
                }

                await message.Channel.SendMessageAsync("📨 Sending reminders in the background…");

                // Run the reminder OUTSIDE the gateway event
                _ = Task.Run(async () =>
                {
                    await SendRegistrationReminderManualAsync(message.Channel);
                });

                return;
            }
        }

        //Manual sender for registration
        private async Task SendRegistrationReminderManualAsync(IMessageChannel originChannel)
        {
            try
            {
                var guild = _client.GetGuild(1109193500664287336);

                ulong hogsRoleId = 1222668156271591485; // LIVE ROLE
                var hogsRole = guild.GetRole(hogsRoleId);

                if (hogsRole == null)
                {
                    await originChannel.SendMessageAsync("❌ HOGS role not found.");
                    return;
                }

                var memberService = _memberService;
                var registered = await memberService.GetAllMembersAsync();
                var registeredIds = registered.Select(m => m.DiscordUserId).ToHashSet();

                var unregistered = hogsRole.Members
                    .Where(u => !registeredIds.Contains(u.Id.ToString()))
                    .ToList();

                if (unregistered.Count == 0)
                {
                    await originChannel.SendMessageAsync("🎉 All HOGS members are already registered!");
                    return;
                }

                // Officer log channel
                ulong officerLogChannelId = 1440209811621937273;
                var officerChannel = _client.GetChannel(officerLogChannelId) as IMessageChannel;

                int sent = 0;
                List<ulong> failed = new();

                foreach (var user in unregistered)
                {
                    try
                    {
                        var dm = await user.CreateDMChannelAsync();
                        await dm.SendMessageAsync(
                            $"👋 Hello **{user.Username}**, you still need to register with the Tribe Bot.\n" +
                            $"Please type `!register` here in DM.\n\n" +
                            $"Registration is required to participate in tribe events.");

                        sent++;

                        await Task.Delay(1200); // safe delay for Discord rate limits
                    }
                    catch
                    {
                        failed.Add(user.Id);

                        if (officerChannel != null)
                            await officerChannel.SendMessageAsync(
                                $"⚠️ Could not DM <@{user.Id}> — Their DMs may be closed.");

                        await Task.Delay(2000); // longer cooldown after a failure
                    }
                }

                // Build final summary for the officer who ran the command
                string result =
                    $"📨 **Manual Registration Reminder Summary**\n\n" +
                    $"• Unregistered members: **{unregistered.Count}**\n" +
                    $"• DMs sent successfully: **{sent}**\n" +
                    $"• Failed deliveries: **{failed.Count}**";

                if (failed.Count > 0)
                {
                    result += "\n\n⚠️ **Could not DM:**\n" +
                              string.Join("\n", failed.Select(id => $"• <@{id}>"));
                }

                await originChannel.SendMessageAsync(result);
            }
            catch (Exception ex)
            {
                await originChannel.SendMessageAsync($"❌ Error sending reminders: {ex.Message}");
            }
        }

        private async Task RemoveMember(SocketMessage message)
        {
            // ============================
            // !removemember @User (OFFICERS ONLY)
            // ============================

            if (message.Content.StartsWith("!removemember", StringComparison.OrdinalIgnoreCase))
            {
                if (message.Channel is not SocketGuildChannel guildChannel)
                {
                    await message.Channel.SendMessageAsync("This command can only be used inside the server.");
                    return;
                }

                var caller = message.Author as SocketGuildUser;
                ulong officerRoleId = 1222665812775534592;

                if (!caller.Roles.Any(r => r.Id == officerRoleId))
                {
                    await message.Channel.SendMessageAsync($"{caller.Mention} you do not have permission to use this command.");
                    return;
                }

                string args = message.Content.Substring("!removemember".Length).Trim();

                if (string.IsNullOrWhiteSpace(args))
                {
                    await message.Channel.SendMessageAsync("Usage: `!removemember @user` or !removemember ingameName`");
                    return;
                }

                var memberService = _memberService;
                var dataStore = _dataStore;

                Member? memberToRemove = null;

                // CASE 1: remove by discord id
                if (message.MentionedUsers.Count > 0)
                {
                    var targetUser = message.MentionedUsers.First();
                    memberToRemove = await memberService.GetMemberByDiscordIdAsync(targetUser.Id.ToString());
                }
                else
                {
                    //CASE 2: Remove by ingame name
                    string ingamename = args;
                    var allMembers = await memberService.GetAllMembersAsync();

                    memberToRemove = allMembers.FirstOrDefault(m =>
                    m.IngameName.Equals(ingamename, StringComparison.OrdinalIgnoreCase));
                }

                if (memberToRemove == null)
                {
                    await message.Channel.SendMessageAsync($"❌ No registered member found matching **{args}**.");
                    return;
                }

                bool success = await dataStore.RemoveMemberByDiscordIdAsync(memberToRemove.DiscordUserId);

                if (success)
                    await message.Channel.SendMessageAsync($"✅ Removed **{memberToRemove.IngameName}** from the member list.");
                else
                    await message.Channel.SendMessageAsync($"❌ Removal failed. Member not found in the sheets.");

                return;
            }
        }


        // ============================
        // !listnonregistered
        // ============================
        private async Task ViewNonRegisteredMembers(SocketMessage message)
        {
            if (message.Content.Equals("!listnonregistered", StringComparison.OrdinalIgnoreCase))
            {
                if (message.Channel is not SocketGuildChannel lnrChan)
                {
                    await message.Channel.SendMessageAsync("This command can only be used in the server.");
                    return;
                }

                var guild = lnrChan.Guild;
                var memberService = _memberService;

                var registered = await memberService.GetAllMembersAsync();
                var registeredIds = registered.Select(m => m.DiscordUserId).ToHashSet();

                ulong hogsRoleId = 1222668156271591485;
                var hogsRole = guild.GetRole(hogsRoleId);

                var nonRegistered = hogsRole.Members
                    .Where(u => !registeredIds.Contains(u.Id.ToString()))
                    .ToList();

                if (nonRegistered.Count == 0)
                {
                    await message.Channel.SendMessageAsync("🎉 Everyone with HOGS role is registered!");
                    return;
                }

                string msg = "❌ **Non-Registered Members**\n\n";

                foreach (var u in nonRegistered)
                    msg += $"• **`{u.DisplayName}`**\n";

                await message.Channel.SendMessageAsync(msg);
                return;
            }
        }


        // ============================================================
        // !myinfo
        // ============================================================
        private async Task MyInfo(SocketMessage message)
        {
            string id = message.Author.Id.ToString();
            var member = await _memberService.GetMemberByDiscordIdAsync(id);

            if (member == null)
            {
                await message.Channel.SendMessageAsync(
                    $"{message.Author.Mention} you are not registered. Use `!register`.");
                return;
            }

            var donations = await _donationService.GetTotalForUserThisWeekAsync(id);

            string donationStatus =
                member.IsExempt ? "🟦 EXEMPT" :
                donations > 0 ? "✅ PAID" : "❌ UNPAID";

            string reply =
                $"🧾 **Your Profile**\n\n" +
                $"**Name:** {member.IngameName}\n" +
                $"**ID:** {member.IngameId}\n" +
                $"**Might:** {member.Might:N0}\n" +
                $"**Kills:** {member.KillPoints:N0}\n" +
                $"**Collector:** {member.CollectorLevel}\n" +
                $"**Reign Points:** {member.ReignPoints}\n" +
                $"**Exempt:** {(member.IsExempt ? "Yes" : "No")}\n" +
                $"**Donation:** {donationStatus}\n" +
                $"**Updated:** {member.LastUpdatedUTC:yyyy-MM-dd HH:mm} UTC";

            await message.Channel.SendMessageAsync(reply);
        }

        // ============================================================
        // !listmembers
        // ============================================================
        private async Task ListMembers(SocketMessage message)
        {
            var members = await _memberService.GetAllMembersAsync();

            if (members.Count == 0)
            {
                await message.Channel.SendMessageAsync("No members found.");
                return;
            }

            string msg = "📜 **Member List (A–Z)**\n\n";

            foreach (var m in members.OrderBy(m => m.IngameName))
                msg += $"• **{m.IngameName}** — `{m.IngameId}`\n";

            await SendLong(message.Channel, msg);
        }

        // ============================================================
        // !viewinfo @user (Officer-only)
        // ============================================================
        private async Task ViewInfo(SocketMessage message)
        {
            if (!IsOfficer(message))
                return;

            if (message.MentionedUsers.Count == 0)
            {
                await message.Channel.SendMessageAsync("Usage: `!viewinfo @user`");
                return;
            }

            var target = message.MentionedUsers.First();
            string id = target.Id.ToString();

            var member = await _memberService.GetMemberByDiscordIdAsync(id);
            if (member == null)
            {
                await message.Channel.SendMessageAsync($"❌ <@{target.Id}> is not registered.");
                return;
            }

            var donations = await _donationService.GetTotalForUserThisWeekAsync(id);

            string donationStatus =
                member.IsExempt ? "🟦 EXEMPT" :
                donations > 0 ? "✅ PAID" : "❌ UNPAID";

            var fines = await _fineService.GetFinesForUserAsync(id);
            var unpaid = fines.Where(f => !f.IsPaid).ToList();
            var paid = fines.Where(f => f.IsPaid).ToList();

            string unpaidText = unpaid.Count == 0
                ? "• None\n"
                : string.Join("", unpaid.Select(f =>
                    $"• {f.Amount:N0} — {f.FineType} — FineID `{f.FineId}` ({f.PaidAmount:N0}/{f.Amount:N0})\n"));

            string paidText = paid.Count == 0
                ? "• None\n"
                : string.Join("", paid.Select(f =>
                    $"• {f.Amount:N0} — {f.FineType} — FineID `{f.FineId}` — PAID\n"));

            string reply =
                $"📘 **Profile for <@{target.Id}>**\n\n" +
                $"**Name:** {member.IngameName}\n" +
                $"**ID:** {member.IngameId}\n" +
                $"**Might:** {member.Might:N0}\n" +
                $"**Kills:** {member.KillPoints:N0}\n" +
                $"**Collector:** {member.CollectorLevel}\n" +
                $"**Reign Points:** {member.ReignPoints}\n" +
                $"**Exempt:** {member.IsExempt}\n\n" +
                $"🏦 **Donation:** {donationStatus}\n\n" +
                $"💀 **Unpaid Fines:**\n{unpaidText}\n" +
                $"🟩 **Paid Fines:**\n{paidText}\n" +
                $"🕒 **Last Updated:** {member.LastUpdatedUTC:yyyy-MM-dd HH:mm} UTC";

            await SendLong(message.Channel, reply);
        }

        // ============================================================
        // HELPERS
        // ============================================================
        private bool IsOfficer(SocketMessage message)
        {
            if (message.Channel is not SocketGuildChannel gc)
            {
                message.Channel.SendMessageAsync("❌ Must be used in guild.");
                return false;
            }

            var user = gc.GetUser(message.Author.Id);
            if (user == null || !user.Roles.Any(r => r.Id == OfficerRoleId))
            {
                message.Channel.SendMessageAsync($"{message.Author.Mention} ❌ No permission.");
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
            string buf = "";
            foreach (var line in lines)
            {
                if ((buf + line).Length > limit)
                {
                    await ch.SendMessageAsync(buf);
                    buf = "";
                }
                buf += line + "\n";
            }

            if (buf.Length > 0)
                await ch.SendMessageAsync(buf);
        }
    }
}
