using TribeBot.Core.Entities;
using TribeBot.Data.Interfaces;
using TribeBot.Services.Interfaces;

namespace TribeBot.Services.Services
{
    public class VoteService : IVoteService
    {
        private readonly IGoogleSheetsDataStore _dataStore;
        private readonly IMemberService _memberService;

        public VoteService(IGoogleSheetsDataStore dataStore, IMemberService memberService)
        {
            _dataStore = dataStore;
            _memberService = memberService;
        }

        // ---------------------------
        // POLLS
        // ---------------------------
        public Task CreatePollAsync(PollRecord poll)
        {
            return _dataStore.AddPollAsync(poll);
        }

        public async Task<PollRecord?> GetPollAsync(string pollId)
        {
            var polls = await _dataStore.GetAllPollsAsync();
            return polls.FirstOrDefault(p => p.PollId == pollId);
        }

        public Task<List<PollRecord>> GetAllPollsAsync()
        {
            return _dataStore.GetAllPollsAsync();
        }

        public async Task RemovePollAsync(string pollId)
        {
            await _dataStore.RemoveVotesForPollAsync(pollId);
            await _dataStore.RemovePollAsync(pollId);
        }

        // ---------------------------
        // VOTES
        // ---------------------------
        public async Task AddOrUpdateVoteAsync(PollVoteRecord vote)
        {
            var existingVotes = await _dataStore.GetVotesForPollAsync(vote.PollId);

            // If user already voted → remove old vote (overwrite)
            var oldVote = existingVotes.FirstOrDefault(v => v.DiscordUserId == vote.DiscordUserId);
            if (oldVote != null)
            {
                // Remove old vote row
                await _dataStore.RemoveVotesForPollAsync(vote.PollId);

                // Re-add all EXCEPT the old vote
                foreach (var v in existingVotes.Where(v => v.DiscordUserId != vote.DiscordUserId))
                {
                    await _dataStore.AddPollVoteAsync(v);
                }
            }

            // Add new / updated vote
            await _dataStore.AddPollVoteAsync(vote);
        }

        public async Task<bool> HasUserVotedAsync(string pollId, string discordUserId)
        {
            var votes = await _dataStore.GetVotesForPollAsync(pollId);
            return votes.Any(v => v.DiscordUserId == discordUserId);
        }

        public Task<List<PollVoteRecord>> GetVotesAsync(string pollId)
        {
            return _dataStore.GetVotesForPollAsync(pollId);
        }

        // ---------------------------
        // RESULTS
        // ---------------------------
        public async Task<Dictionary<string, int>> GetAnonymousResultsAsync(string pollId)
        {
            var votes = await _dataStore.GetVotesForPollAsync(pollId);

            return votes
                .GroupBy(v => v.Choice)
                .ToDictionary(g => g.Key, g => g.Count());
        }

        public async Task<Dictionary<string, List<PollVoteRecord>>> GetOfficerResultsAsync(string pollId)
        {
            var votes = await _dataStore.GetVotesForPollAsync(pollId);

            return votes
                .GroupBy(v => v.Choice)
                .ToDictionary(g => g.Key, g => g.ToList());
        }
    }
}
