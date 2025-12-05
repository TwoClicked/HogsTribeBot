using TribeBot.Core.Entities;

namespace TribeBot.Core.Interfaces
{
    public interface IVoteService
    {
        Task CreatePollAsync(PollRecord poll);
        Task<PollRecord?> GetPollAsync(string pollId);
        Task<List<PollRecord>> GetAllPollsAsync();
        Task RemovePollAsync(string pollId);

        Task AddOrUpdateVoteAsync(PollVoteRecord vote);
        Task<bool> HasUserVotedAsync(string pollId, string discordUserId);
        Task<List<PollVoteRecord>> GetVotesAsync(string pollId);

        Task<Dictionary<string, int>> GetAnonymousResultsAsync(string pollId);
        Task<Dictionary<string, List<PollVoteRecord>>> GetOfficerResultsAsync(string pollId);
    }
}
