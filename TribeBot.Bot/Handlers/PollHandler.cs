using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TribeBot.Core.Entities;
using TribeBot.Core.Interfaces;
using TribeBot.Bot.UI; // <-- IMPORTANT for EmbedHelper

namespace TribeBot.Bot.Handlers
{
    public class PollHandler
    {
        private readonly DiscordSocketClient _client;
        private readonly IVoteService _voteService;
        private readonly IMemberService _memberService;

        private const ulong OfficerRoleId = 1222665812775534592;
        private const ulong GuildId = 1109193500664287336;
        private const ulong HogsRole = 1222668156271591485;
        private const ulong OfficerLogChannelId = 1440211043820507217;

        public PollHandler(
            DiscordSocketClient client,
            IVoteService voteService,
            IMemberService memberService)
        {
            _client = client;
            _voteService = voteService;
            _memberService = memberService;
        }

        private IMessageChannel OfficerLog =>
            _client.GetChannel(OfficerLogChannelId) as IMessageChannel;

        private Task Log(string title, Dictionary<string, string> fields)
            => OfficerLog?.SendMessageAsync(embed: EmbedHelper.Log(title, fields))
               ?? Task.CompletedTask;

        // ============================================================================
        // ROOT ENTRY
        // ============================================================================
        public async Task<bool> TryHandleAsync(SocketMessage message)
        {
            if (message.Author.IsBot) return false;

            string content = message.Content.Trim().ToLower();

            if (content.StartsWith("!pollcreate"))
            {
                await CreatePoll(message);
                return true;
            }

            if (content == "!polllist")
            {
                await ListPolls(message);
                return true;
            }

            if (content.StartsWith("!pollremove"))
            {
                await RemovePoll(message);
                return true;
            }

            if (content.StartsWith("!pollshow"))
            {
                await ShowPoll(message);
                return true;
            }

            if (content.StartsWith("!pollofficer"))
            {
                await OfficerResults(message);
                return true;
            }

            // DM voting only
            if (message.Channel is IDMChannel && content.StartsWith("!vote "))
            {
                await SubmitVote(message);
                return true;
            }

            return false;
        }

        // ============================================================================
        // CREATE POLL
        // ============================================================================
        private async Task CreatePoll(SocketMessage message)
        {
            if (!IsOfficer(message)) return;

            var quoted = System.Text.RegularExpressions.Regex
                .Matches(message.Content, "\"([^\"]+)\"")
                .Select(m => m.Groups[1].Value).ToList();

            if (quoted.Count < 3)
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error(
                        "You must provide a question, date, and at least **2 options**.\n\n" +
                        "**Example:**\n" +
                        "`!pollcreate \"Your question\" 2025-03-01 \"Option A\" \"Option B\"`"
                    ));
                return;
            }

            string question = quoted[0];

            // Extract date
            string after = message.Content.Substring(message.Content.IndexOf(question) + question.Length + 2);
            var dateMatch = System.Text.RegularExpressions.Regex.Match(after, @"\d{4}-\d{2}-\d{2}");

            if (!dateMatch.Success || !DateTime.TryParse(dateMatch.Value, out DateTime endDate))
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error("Invalid or missing date. Use: `YYYY-MM-DD`"));
                return;
            }

            var options = quoted.Skip(1).ToList();
            string pollId = Guid.NewGuid().ToString("N")[..8];

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

            await message.Channel.SendMessageAsync(embed:
                EmbedHelper.Info("Poll Created",
                    $"📊 **New Poll Created**\n\n" +
                    $"**ID:** `{pollId}`\n" +
                    $"**Question:** {question}\n" +
                    $"**Ends:** `{endDate:yyyy-MM-dd}`\n\n" +
                    $"DM ballots are being delivered…"
                ));

            _ = Task.Run(async () => await SendDMBallots(poll));
        }

        // ============================================================================
        // SEND DM BALLOTS (plaintext — correct)
        // ============================================================================
        private async Task SendDMBallots(PollRecord poll)
        {
            var guild = _client.GetGuild(GuildId);
            var hogs = guild.GetRole(HogsRole);

            var registered = await _memberService.GetAllMembersAsync();
            var valid = registered.Select(x => x.DiscordUserId).ToHashSet();

            var targets = hogs.Members.Where(x => valid.Contains(x.Id.ToString())).ToList();

            int sent = 0;
            int failed = 0;

            foreach (var u in targets)
            {
                try
                {
                    var dm = await u.CreateDMChannelAsync();

                    string opt = "";
                    for (int i = 0; i < poll.Options.Count; i++)
                        opt += $"{i + 1}) {poll.Options[i]}\n";

                    await dm.SendMessageAsync(
                        $"📊 **New Poll**\n" +
                        $"**{poll.Question}**\n\n" +
                        "**Options:**\n" + opt + "\n" +
                        "Vote using:\n`!vote <number>`\n\n" +
                        $"Poll ID: `{poll.PollId}`\n" +
                        $"Ends: {poll.EndDateUtc:yyyy-MM-dd}"
                    );

                    sent++;
                    await Task.Delay(350);

                }
                catch
                {
                    failed++;
                    await Log("Poll DM Failure", new()
                    {
                        { "User", u.Id.ToString() },
                        { "Poll", poll.PollId }
                    });

                    await Task.Delay(650);
                }
            }

            await Log("Poll DM Summary", new()
            {
                { "Poll", poll.PollId },
                { "Question", poll.Question },
                { "DM Sent", sent.ToString() },
                { "Failed", failed.ToString() },
                { "Total", targets.Count.ToString() }
            });
        }

        // ============================================================================
        // LIST POLLS
        // ============================================================================
        private async Task ListPolls(SocketMessage message)
        {
            var polls = await _voteService.GetAllPollsAsync();

            if (!polls.Any())
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Warning("No polls currently exist."));
                return;
            }

            string msg = "📋 **Poll List**\n\n";

            foreach (var p in polls.OrderBy(p => p.EndDateUtc))
            {
                string status = p.EndDateUtc < DateTime.UtcNow ? "❌ ENDED" : "🟢 ACTIVE";

                msg +=
                    $"• **{p.Question}**\n" +
                    $"  ID: `{p.PollId}` — {status}\n" +
                    $"  Ends: `{p.EndDateUtc:yyyy-MM-dd}`\n\n";
            }

            await message.Channel.SendMessageAsync(msg);
        }

        // ============================================================================
        // REMOVE POLL
        // ============================================================================
        private async Task RemovePoll(SocketMessage message)
        {
            if (!IsOfficer(message)) return;

            var parts = message.Content.Split(" ", 2);
            if (parts.Length < 2)
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Warning("Usage: `!pollremove <pollId>`"));
                return;
            }

            string pollId = parts[1].Trim();
            await _voteService.RemovePollAsync(pollId);

            await message.Channel.SendMessageAsync(embed:
                EmbedHelper.Success($"Poll `{pollId}` has been removed."));
        }

        // ============================================================================
        // SHOW POLL RESULTS
        // ============================================================================
        private async Task ShowPoll(SocketMessage message)
        {
            var parts = message.Content.Split(" ", 2);
            if (parts.Length < 2)
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Warning("Usage: `!pollshow <pollId>`"));
                return;
            }

            string pollId = parts[1].Trim();
            var poll = await _voteService.GetPollAsync(pollId);

            if (poll == null)
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error("Poll not found."));
                return;
            }

            var results = await _voteService.GetAnonymousResultsAsync(pollId);

            string body = $"Ends: `{poll.EndDateUtc:yyyy-MM-dd}`\n\n";

            foreach (var opt in poll.Options)
            {
                results.TryGetValue(opt, out int count);
                body += $"• **{opt}** — {count} votes\n";
            }

            body += $"\n**Total Votes:** {results.Values.Sum()}";

            await message.Channel.SendMessageAsync(embed:
                EmbedHelper.Info("Poll Results", $"**{poll.Question}**\n\n{body}"));
        }

        // ============================================================================
        // OFFICER POLL RESULTS
        // ============================================================================
        private async Task OfficerResults(SocketMessage message)
        {
            if (!IsOfficer(message)) return;

            var parts = message.Content.Split(" ", 2);
            if (parts.Length < 2)
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Warning("Usage: `!pollofficer <pollId>`"));
                return;
            }

            string pollId = parts[1].Trim();
            var poll = await _voteService.GetPollAsync(pollId);

            if (poll == null)
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error("Poll not found."));
                return;
            }

            // Officer results are usually VERY long → raw text is correct here
            var results = await _voteService.GetOfficerResultsAsync(pollId);

            string msg =
                $"📊 **Officer Results — {poll.Question}**\n" +
                $"Ends: {poll.EndDateUtc:yyyy-MM-dd}\n\n";

            foreach (var opt in poll.Options)
            {
                msg += $"**{opt}:**\n";

                if (!results.ContainsKey(opt) || results[opt].Count == 0)
                {
                    msg += "• No votes\n\n";
                    continue;
                }

                foreach (var voter in results[opt])
                    msg += $"• {voter.IngameName} (<@{voter.DiscordUserId}>)\n";

                msg += "\n";
            }

            await message.Channel.SendMessageAsync(msg);
        }

        // ============================================================================
        // SUBMIT VOTE (DM ONLY)
        // ============================================================================
        private async Task SubmitVote(SocketMessage message)
        {
            var parts = message.Content.Split(" ");

            if (parts.Length < 2 || !int.TryParse(parts[1], out int index))
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error("Usage: `!vote <number>`"));
                return;
            }

            var polls = await _voteService.GetAllPollsAsync();
            var active = polls
                .Where(p => p.EndDateUtc > DateTime.UtcNow)
                .OrderByDescending(p => p.CreatedAtUtc)
                .FirstOrDefault();

            if (active == null)
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error("No active poll to vote in."));
                return;
            }

            if (index < 1 || index > active.Options.Count)
            {
                await message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error("Invalid option number."));
                return;
            }

            var member = await _memberService.GetMemberByDiscordIdAsync(message.Author.Id.ToString());
            string choice = active.Options[index - 1];

            var vote = new PollVoteRecord
            {
                PollId = active.PollId,
                Choice = choice,
                DiscordUserId = message.Author.Id.ToString(),
                IngameName = member?.IngameName ?? "",
                TimestampUtc = DateTime.UtcNow
            };

            await _voteService.AddOrUpdateVoteAsync(vote);

            await message.Channel.SendMessageAsync(embed:
                EmbedHelper.Success($"Your vote has been recorded.\n**Choice:** {choice}"));
        }

        // ============================================================================
        // HELPERS
        // ============================================================================
        private bool IsOfficer(SocketMessage message)
        {
            if (message.Channel is not SocketGuildChannel)
            {
                _ = message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error("This command must be used inside the server."));
                return false;
            }

            var user = (message.Channel as SocketGuildChannel)!.GetUser(message.Author.Id);

            if (user == null || !user.Roles.Any(r => r.Id == OfficerRoleId))
            {
                _ = message.Channel.SendMessageAsync(embed:
                    EmbedHelper.Error($"{message.Author.Mention}, you do not have permission."));
                return false;
            }

            return true;
        }
    }
}
