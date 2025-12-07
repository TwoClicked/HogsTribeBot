using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TribeBot.Core.Entities;
using TribeBot.Core.Interfaces;

namespace TribeBot.Bot.Handlers
{
    public class PollHandler
    {
        private readonly DiscordSocketClient _client;
        private readonly IVoteService _voteService;
        private readonly IMemberService _memberService;

        // CONFIG
        private const ulong OfficerRoleId = 1222665812775534592;
        private const ulong GuildId = 1109193500664287336;
        private const ulong HogsRole = 1439972286877794314; // HOGS role for DM sending
        private const ulong OfficerLogChannelId = 1440209811621937273;

        public PollHandler(
            DiscordSocketClient client,
            IVoteService voteService,
            IMemberService memberService)
        {
            _client = client;
            _voteService = voteService;
            _memberService = memberService;
        }

        // =========================================================================
        // ENTRY POINT
        // =========================================================================
        public async Task<bool> TryHandleAsync(SocketMessage message)
        {
            if (message.Author.IsBot)
                return false;

            string content = message.Content.Trim();

            // Commands used in SERVER:
            if (content.StartsWith("!pollcreate", StringComparison.OrdinalIgnoreCase))
            {
                await CreatePoll(message);
                return true;
            }

            if (content.Equals("!polllist", StringComparison.OrdinalIgnoreCase))
            {
                await ListPolls(message);
                return true;
            }

            if (content.StartsWith("!pollremove", StringComparison.OrdinalIgnoreCase))
            {
                await RemovePoll(message);
                return true;
            }

            if (content.StartsWith("!pollshow", StringComparison.OrdinalIgnoreCase))
            {
                await ShowPoll(message);
                return true;
            }

            if (content.StartsWith("!pollofficer", StringComparison.OrdinalIgnoreCase))
            {
                await OfficerResults(message);
                return true;
            }

            // DM voting only
            if (message.Channel is IDMChannel && content.StartsWith("!vote ", StringComparison.OrdinalIgnoreCase))
            {
                await SubmitVote(message);
                return true;
            }

            return false;
        }

        // =========================================================================
        // 1. !pollcreate "question" YYYY-MM-DD "opt1" "opt2" ...
        // =========================================================================
        private async Task CreatePoll(SocketMessage message)
        {
            if (!IsOfficer(message))
                return;

            var parts = message.Content;
            var quoted = System.Text.RegularExpressions.Regex.Matches(parts, "\"([^\"]+)\"")
                .Select(m => m.Groups[1].Value).ToList();

            if (quoted.Count < 3)
            {
                await message.Channel.SendMessageAsync(
                    "❌ You must provide a question, date, and at least 2 options.\n" +
                    "Example:\n" +
                    "`!pollcreate \"Your question\" 2025-03-01 \"Option A\" \"Option B\"`");
                return;
            }

            string question = quoted[0];

            // Extract date after question
            string after = parts.Substring(parts.IndexOf(quoted[0]) + quoted[0].Length + 2);
            var dateMatch = System.Text.RegularExpressions.Regex.Match(after, @"\d{4}-\d{2}-\d{2}");

            if (!dateMatch.Success || !DateTime.TryParse(dateMatch.Value, out DateTime endDate))
            {
                await message.Channel.SendMessageAsync("❌ Invalid or missing date. Use YYYY-MM-DD.");
                return;
            }

            var options = quoted.Skip(1).ToList();
            string pollId = Guid.NewGuid().ToString("N").Substring(0, 8);

            var poll = new PollRecord
            {
                PollId = pollId,
                Question = question,
                EndDateUtc = endDate.ToUniversalTime(),
                Options = options,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByDiscordId = message.Author.Id.ToString()
            };

            await _voteService.CreatePollAsync(poll);

            await message.Channel.SendMessageAsync(
                $"📊 **Poll Created**\n" +
                $"ID: `{pollId}`\n" +
                $"Question: **{question}**\n" +
                $"Ends: `{endDate:yyyy-MM-dd}`\n" +
                $"Sending DM ballots…");

            // Offload DM sending
            _ = Task.Run(async () => await SendDMBallots(poll));
        }

        // =========================================================================
        // Send DM ballots to members with HOGS role
        // =========================================================================
        private async Task SendDMBallots(PollRecord poll)
        {
            var guild = _client.GetGuild(GuildId);
            var role = guild.GetRole(HogsRole);

            var members = await _memberService.GetAllMembersAsync();
            var validIds = members.Select(m => m.DiscordUserId).ToHashSet();

            var targets = role.Members.Where(m => validIds.Contains(m.Id.ToString())).ToList();

            int sent = 0;
            int failed = 0;

            foreach (var user in targets)
            {
                try
                {
                    var dm = await user.CreateDMChannelAsync();

                    string optionList = "";
                    for (int i = 0; i < poll.Options.Count; i++)
                        optionList += $"{i + 1}) {poll.Options[i]}\n";

                    await dm.SendMessageAsync(
                        $"📊 **New Poll**\n" +
                        $"**{poll.Question}**\n\n" +
                        "**Options:**\n" +
                        optionList + "\n" +
                        "Vote by replying:\n" +
                        "`!vote <number>`\n\n" +
                        $"Poll ID: `{poll.PollId}`\n" +
                        $"Ends: {poll.EndDateUtc:yyyy-MM-dd}");

                    sent++;
                    await Task.Delay(1200);
                }
                catch
                {
                    failed++;
                    await LogOfficer($"⚠️ Could not DM <@{user.Id}> for poll `{poll.PollId}`.");
                    await Task.Delay(1500);
                }
            }

            await LogOfficer(
                $"📩 **Poll DM Summary**\n" +
                $"Poll ID: `{poll.PollId}`\n" +
                $"Question: {poll.Question}\n" +
                $"DMs Sent: **{sent}**\n" +
                $"Failed: **{failed}**\n" +
                $"Total: **{targets.Count}**");
        }

        // =========================================================================
        // 2. !polllist
        // =========================================================================
        private async Task ListPolls(SocketMessage message)
        {
            var polls = await _voteService.GetAllPollsAsync();

            if (polls.Count == 0)
            {
                await message.Channel.SendMessageAsync("No polls exist.");
                return;
            }

            string response = "📋 **Poll List**\n\n";

            foreach (var p in polls.OrderBy(p => p.EndDateUtc))
            {
                string status = p.EndDateUtc < DateTime.UtcNow ? "❌ ENDED" : "🟢 ACTIVE";

                response +=
                    $"• ID: `{p.PollId}` — {status}\n" +
                    $"  Q: {p.Question}\n" +
                    $"  Ends: {p.EndDateUtc:yyyy-MM-dd}\n\n";
            }

            await message.Channel.SendMessageAsync(response);
        }

        // =========================================================================
        // 3. !pollremove <pollId>
        // =========================================================================
        private async Task RemovePoll(SocketMessage message)
        {
            if (!IsOfficer(message)) return;

            var parts = message.Content.Split(" ", 2);
            if (parts.Length < 2)
            {
                await message.Channel.SendMessageAsync("Usage: !pollremove <pollId>");
                return;
            }

            string pollId = parts[1].Trim();

            await _voteService.RemovePollAsync(pollId);
            await message.Channel.SendMessageAsync($"🗑️ Removed poll `{pollId}`.");
        }

        // =========================================================================
        // 4. !pollshow <pollId> (user-friendly results)
        // =========================================================================
        private async Task ShowPoll(SocketMessage message)
        {
            var parts = message.Content.Split(" ", 2);
            if (parts.Length < 2)
            {
                await message.Channel.SendMessageAsync("Usage: !pollshow <pollId>");
                return;
            }

            string pollId = parts[1].Trim();
            var poll = await _voteService.GetPollAsync(pollId);

            if (poll == null)
            {
                await message.Channel.SendMessageAsync("Poll not found.");
                return;
            }

            var results = await _voteService.GetAnonymousResultsAsync(pollId);

            string output =
                $"📊 **Poll Results: {poll.Question}**\n" +
                $"Ends: {poll.EndDateUtc:yyyy-MM-dd}\n\n";

            foreach (var option in poll.Options)
            {
                results.TryGetValue(option, out int count);
                output += $"• **{option}** — {count} votes\n";
            }

            int total = results.Values.Sum();
            output += $"\nTotal votes: **{total}**";

            await message.Channel.SendMessageAsync(output);
        }

        // =========================================================================
        // 5. !pollofficer <pollId> (officer-only detailed view)
        // =========================================================================
        private async Task OfficerResults(SocketMessage message)
        {
            if (!IsOfficer(message))
                return;

            var parts = message.Content.Split(" ", 2);
            if (parts.Length < 2)
            {
                await message.Channel.SendMessageAsync("Usage: !pollofficer <pollId>");
                return;
            }

            string pollId = parts[1].Trim();
            var poll = await _voteService.GetPollAsync(pollId);

            if (poll == null)
            {
                await message.Channel.SendMessageAsync("Poll not found.");
                return;
            }

            var results = await _voteService.GetOfficerResultsAsync(pollId);

            string response =
                $"📊 **Officer View — {poll.Question}**\n" +
                $"Ends: {poll.EndDateUtc:yyyy-MM-dd}\n\n";

            foreach (var option in poll.Options)
            {
                response += $"**{option}:**\n";

                if (!results.ContainsKey(option) || results[option].Count == 0)
                {
                    response += "• No votes\n\n";
                    continue;
                }

                foreach (var voter in results[option])
                {
                    response += $"• {voter.IngameName} (<@{voter.DiscordUserId}>)\n";
                }

                response += "\n";
            }

            await message.Channel.SendMessageAsync(response);
        }

        // =========================================================================
        // 6. !vote <option> (DM ONLY)
        // =========================================================================
        private async Task SubmitVote(SocketMessage message)
        {
            var parts = message.Content.Split(" ");
            if (parts.Length < 2 || !int.TryParse(parts[1], out int optionIndex))
            {
                await message.Channel.SendMessageAsync("❌ Usage: `!vote <number>`");
                return;
            }

            var polls = await _voteService.GetAllPollsAsync();
            var activePoll = polls
                .Where(p => p.EndDateUtc > DateTime.UtcNow)
                .OrderByDescending(p => p.CreatedAtUtc)
                .FirstOrDefault();

            if (activePoll == null)
            {
                await message.Channel.SendMessageAsync("❌ No active poll.");
                return;
            }

            if (optionIndex < 1 || optionIndex > activePoll.Options.Count)
            {
                await message.Channel.SendMessageAsync("❌ Invalid option.");
                return;
            }

            var member = await _memberService.GetMemberByDiscordIdAsync(message.Author.Id.ToString());
            string choice = activePoll.Options[optionIndex - 1];

            var vote = new PollVoteRecord
            {
                PollId = activePoll.PollId,
                Choice = choice,
                DiscordUserId = message.Author.Id.ToString(),
                IngameName = member?.IngameName ?? "",
                TimestampUtc = DateTime.UtcNow
            };

            await _voteService.AddOrUpdateVoteAsync(vote);

            await message.Channel.SendMessageAsync(
                $"🗳️ **Vote recorded!**\nYou voted for: **{choice}**");
        }

        // =========================================================================
        // HELPERS
        // =========================================================================
        private bool IsOfficer(SocketMessage message)
        {
            if (message.Channel is not SocketGuildChannel gc)
            {
                message.Channel.SendMessageAsync("❌ Use this inside the server.");
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

        private async Task LogOfficer(string msg)
        {
            var log = _client.GetChannel(OfficerLogChannelId) as IMessageChannel;
            if (log != null)
                await log.SendMessageAsync(msg);
        }
    }
}
