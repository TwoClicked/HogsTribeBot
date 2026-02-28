using Discord;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;
using TribeBot.Core.Interfaces;
using TribeBot.Data.Interfaces;
using TribeBot.Bot.UI;

namespace TribeBot.Bot.Handlers
{
    public class ReignHandler
    {
        private readonly DiscordSocketClient _client;
        private readonly IReignService _reignService;
        private readonly IFineService _fineService;
        private readonly IMemberService _memberService;
        private readonly IGoogleSheetsDataStore _dataStore;

        private const ulong GuildId = 1109193500664287336;
        private const ulong ReignOfficerRoleId = 1364209274322157639;
        private const ulong VrSubmissionChannelId = 1429640756104265829;
        private const ulong OfficerLogChannelId = 1440211043820507217;
        private const ulong ReignParticipantRoleId = 1326014354331664435;

        public ReignHandler(
            DiscordSocketClient client,
            IReignService reignService,
            IFineService fineService,
            IMemberService memberService,
            IGoogleSheetsDataStore dataStore)
        {
            _client = client;
            _reignService = reignService;
            _fineService = fineService;
            _memberService = memberService;
            _dataStore = dataStore;
        }

        private IMessageChannel OfficerLog =>
            _client.GetChannel(OfficerLogChannelId) as IMessageChannel;

        private Task Log(string title, string user, string action)
            => OfficerLog?.SendMessageAsync(embed:
                EmbedHelper.Log(title, new()
                {
                    { "User", user },
                    { "Action", action }
                })) ?? Task.CompletedTask;

        // ======================================================================
        // ROOT HANDLER
        // ======================================================================
        public async Task<bool> TryHandleAsync(SocketMessage message)
        {
            if (message.Author.IsBot)
                return false;

            string content = message.Content.Trim();

            switch (content.ToLower())
            {
                case "!applyreign":
                    await ApplyReign(message);
                    return true;

                case "!listreign":
                    await ListReign(message);
                    return true;

                case "!clearreign":
                    await ClearReign(message);
                    return true;

                case "!lockreign":
                    await LockReign(message);
                    return true;

                case "!unlockreign":
                    await UnlockReign(message);
                    return true;

                case "!leavereign":
                    await LeaveReign(message);
                    return true;
            }

            if (content.StartsWith("!exempt", StringComparison.OrdinalIgnoreCase))
            {
                await SetExempt(message, true);
                return true;
            }

            if (content.StartsWith("!unexempt", StringComparison.OrdinalIgnoreCase))
            {
                await SetExempt(message, false);
                return true;
            }

            if (content.StartsWith("!removereign", StringComparison.OrdinalIgnoreCase))
            {
                await RemoveReignMember(message);
                return true;
            }

            if (content.StartsWith("!setreignplayer", StringComparison.OrdinalIgnoreCase))
            {
                await SetReignPlayer(message);
                return true;
            }

            if (content.StartsWith("!setreignpoints", StringComparison.OrdinalIgnoreCase))
            {
                await HandleSetReignPoints(message);
                return true;
            }

            return false;
        }

        // ======================================================================
        // !setreignplayer
        // ======================================================================
        private async Task SetReignPlayer(SocketMessage message)
        {
            if (!await IsOfficer(message))
                return;

            if (message.MentionedUsers.Count == 0)
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error("Usage: `!setreignplayer @user`"));
                return;
            }

            var target = message.MentionedUsers.First();

            var member = await _memberService
                .GetMemberByDiscordIdAsync(target.Id.ToString());

            if (member == null)
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error($"{target.Username} is not registered."));
                return;
            }

            if (await _reignService.GetReignLockedAsync())
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Warning("Reign is currently locked."));
                return;
            }

            // Check if already applied
            var current = await _reignService
                .GetCurrentRegistrationsSortedAsync();

            if (current.Any(x => x.member.DiscordUserId == target.Id.ToString()))
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Warning($"{target.Username} is already in the reign."));
                return;
            }

            await _reignService.ApplyAsync(target.Id.ToString());

            await message.Channel.SendMessageAsync(embed:
                EmbedHelper.Success(
                    $"{target.Username} has been added to the Viking Reign 👑"));

            await Log(
                "Reign Update — Officer Addition",
                target.Username,
                $"Added by {message.Author.Username}");
        }

        // ======================================================================
        // !setreignpoints
        // ======================================================================
        private async Task HandleSetReignPoints(SocketMessage message)
        {
            if (!await IsOfficer(message))
                return;

            if (message.MentionedUsers.Count == 0)
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error("Usage: `!setreignpoints @user <points>`"));
                return;
            }

            var parts = message.Content
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 3 || !long.TryParse(parts[2], out long points))
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error("Please provide a valid number of points."));
                return;
            }

            var target = message.MentionedUsers.First();

            var success = await _memberService
                .SetReignPointsAsync(target.Id.ToString(), points);

            if (!success)
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error("Member not found."));
                return;
            }

            await message.Channel.SendMessageAsync(embed:
                EmbedHelper.Success(
                    $"{target.Username} now has **{points} Reign Points** 👑"));

            await Log(
                "Reign Points Updated",
                target.Username,
                $"Set to {points} by {message.Author.Username}");
        }

        // ======================================================================
        // !leavereign
        // ======================================================================
        private async Task LeaveReign(SocketMessage message)
        {
            var member = await _memberService
                .GetMemberByDiscordIdAsync(message.Author.Id.ToString());

            if (member == null)
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error("You are not registered."));
                return;
            }

            bool removed = await _reignService
                .RemoveMemberFromReignAsync(message.Author.Id.ToString());

            if (!removed)
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error("You are not part of the current reign."));
                return;
            }

            await message.Channel.SendMessageAsync(embed:
                EmbedHelper.Success(
                    $"{message.Author.Username}, you have left the current reign."));

            await Log(
                "Reign Update — Self Removal",
                message.Author.Username,
                "Left the reign");
        }

        // ======================================================================
        // !removereign
        // ======================================================================
        private async Task RemoveReignMember(SocketMessage message)
        {
            if (!await IsOfficer(message))
                return;

            if (message.MentionedUsers.Count == 0)
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error("Usage: `!removereign @user`"));
                return;
            }

            var target = message.MentionedUsers.First();

            bool removed = await _reignService
                .RemoveMemberFromReignAsync(target.Id.ToString());

            if (!removed)
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error(
                        $"{target.Username} is not in the current reign."));
                return;
            }

            await message.Channel.SendMessageAsync(embed:
                EmbedHelper.Success(
                    $"{target.Username} has been removed from the reign."));

            await Log(
                "Reign Update — Officer Removal",
                target.Username,
                $"Removed by {message.Author.Username}");
        }

        // ======================================================================
        // !applyreign
        // ======================================================================
        private async Task ApplyReign(SocketMessage message)
        {
            if (await _reignService.GetReignLockedAsync())
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Warning("Reign is locked. Contact an officer."));
                return;
            }

            if (message.Channel.Id != VrSubmissionChannelId)
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error(
                        $"You may only apply in <#{VrSubmissionChannelId}>."));
                return;
            }

            var fines = await _fineService
                .GetFinesForUserAsync(message.Author.Id.ToString());

            int activeStrikes = fines
                .Where(f => f.FineType == "Reign" && !f.IsPaid)
                .Sum(f => f.ReignStrikes);

            if (activeStrikes > 0)
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error(
                        $"You have **{activeStrikes} Reign Strike(s)**."));
                return;
            }

            var member = await _memberService
                .GetMemberByDiscordIdAsync(message.Author.Id.ToString());

            if (member == null)
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error(
                        "Please register first using `!register`."));
                return;
            }

            await _reignService.ApplyAsync(message.Author.Id.ToString());

            await message.Channel.SendMessageAsync(embed:
                EmbedHelper.Success(
                    "You have been added to the Viking Reign!"));
        }

        // ======================================================================
        // !listreign
        // ======================================================================
        private async Task ListReign(SocketMessage message)
        {
            var results = await _reignService
                .GetCurrentRegistrationsSortedAsync();

            if (results.Count == 0)
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Info(
                        "Viking Reign",
                        "Nobody has applied yet."));
                return;
            }

            int pos = 1;
            string msg = "";

            foreach (var (member, _) in results)
            {
                msg += $"**{pos})** {member.IngameName} — {member.ReignPoints} pts\n";
                pos++;
            }

            await message.Channel.SendMessageAsync(embed:
                EmbedHelper.Info("Viking Reign Applicants", msg));
        }

        // ======================================================================
        // !clearreign
        // ======================================================================
        private async Task ClearReign(SocketMessage message)
        {
            if (!await IsOfficer(message))
                return;

            if (!await _reignService.GetReignLockedAsync())
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Warning(
                        "There is no active reign to clear."));
                return;
            }

            if (message.Channel is not SocketGuildChannel sg)
                return;

            var guild = sg.Guild;
            var role = guild.GetRole(ReignParticipantRoleId);

            if (role != null)
            {
                foreach (var user in role.Members)
                {
                    await user.RemoveRoleAsync(role);
                }
            }

            await _reignService.ClearAsync();
            await _reignService.SetReignLockedAsync(false);

            await message.Channel.SendMessageAsync(embed:
                EmbedHelper.Success(
                    "Reign cleared and roles removed."));

            await Log(
                "Reign Cleared",
                message.Author.Username,
                "Roles removed and list reset");
        }

        // ======================================================================
        // !lockreign
        // ======================================================================
        private async Task LockReign(SocketMessage message)
        {
            if (!await IsOfficer(message))
                return;

            if (await _reignService.GetReignLockedAsync())
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Warning(
                        "The reign is already locked."));
                return;
            }

            await _reignService.SetReignLockedAsync(true);
            await _fineService.ReduceReignStrikesAsync();

            var topReign = (await _reignService
                .GetCurrentRegistrationsSortedAsync())
                .Take(20)
                .ToList();

            if (message.Channel is not SocketGuildChannel sg)
                return;

            var guild = sg.Guild;
            var role = guild.GetRole(ReignParticipantRoleId);

            if (role == null)
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error(
                        "Reign role not found."));
                return;
            }

            foreach (var (member, _) in topReign)
            {
                if (!ulong.TryParse(member.DiscordUserId, out var userId))
                    continue;

                var guildUser = guild.GetUser(userId);
                if (guildUser == null)
                    continue;

                if (!guildUser.Roles.Any(r => r.Id == role.Id))
                {
                    await guildUser.AddRoleAsync(role);
                }
            }

            await message.Channel.SendMessageAsync(embed:
                EmbedHelper.Warning(
                    "The reign is now **LOCKED**. Top 20 have received the role."));

            await Log(
                "Reign Locked",
                message.Author.Username,
                "Top 20 roles assigned");
        }

        // ======================================================================
        // !unlockreign
        // ======================================================================
        private async Task UnlockReign(SocketMessage message)
        {
            if (!await IsOfficer(message))
                return;

            if (!await _reignService.GetReignLockedAsync())
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Warning(
                        "The reign is already unlocked."));
                return;
            }

            await _reignService.SetReignLockedAsync(false);

            await message.Channel.SendMessageAsync(embed:
                EmbedHelper.Success(
                    "The reign is now **UNLOCKED**."));
        }

        // ======================================================================
        // !exempt / !unexempt
        // ======================================================================
        private async Task SetExempt(SocketMessage message, bool exempt)
        {
            if (!await IsOfficer(message))
                return;

            if (message.MentionedUsers.Count == 0)
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error(
                        "Usage: `!exempt @user` or `!unexempt @user`"));
                return;
            }

            var target = message.MentionedUsers.First();

            var member = await _memberService
                .GetMemberByDiscordIdAsync(target.Id.ToString());

            if (member == null)
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error(
                        $"{target.Username} is not registered."));
                return;
            }

            member.IsExempt = exempt;
            member.LastUpdatedUTC = DateTime.UtcNow;

            await _memberService.RegisterOrUpdateAsync(member);

            string status = exempt ? "EXEMPT" : "NOT EXEMPT";

            await message.Channel.SendMessageAsync(embed:
                EmbedHelper.Success(
                    $"{target.Username} is now **{status}** from weekly donations."));
        }

        // ======================================================================
        // Officer Check
        // ======================================================================
        private async Task<bool> IsOfficer(SocketMessage message)
        {
            if (message.Channel is not SocketGuildChannel sg)
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error(
                        "This command must be used inside the server."));
                return false;
            }

            var user = sg.GetUser(message.Author.Id);

            if (user == null ||
                !user.Roles.Any(r => r.Id == ReignOfficerRoleId))
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error(
                        "You do not have permission to perform this action."));
                return false;
            }

            return true;
        }
    }
}
