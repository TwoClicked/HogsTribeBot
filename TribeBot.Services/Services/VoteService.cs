using TribeBot.Core.Entities;
using TribeBot.Data.Interfaces;
using TribeBot.Core.Interfaces;


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
        // Singular Vote adjustment (Remove or add) 
        // ---------------------------
        public async Task AddOrUpdateVoteAsync(PollVoteRecord vote)
        {

            // Check if user already voted (One single read)
            var oldVote = await _dataStore.GetVoteAsync(vote.PollId, vote.DiscordUserId);

            if (oldVote != null)
            {
                // Remove only one row (No read needed) 
                await _dataStore.RemoveVoteAsync(vote.PollId, vote.DiscordUserId);
            }

            // Insert new vote (No read needed) 
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
