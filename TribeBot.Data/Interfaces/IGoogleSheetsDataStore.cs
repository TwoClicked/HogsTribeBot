using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TribeBot.Core.Entities;

namespace TribeBot.Data.Interfaces
{
    public interface IGoogleSheetsDataStore
    {
        // Members
        Task<Member?> GetMemberAsync(string discordUserId);
        Task<List<Member>> GetAllMembersAsync();
        Task SaveMemberAsync(Member member);
        Task<bool> RemoveMemberByDiscordIdAsync(string discordId);


        // Reign registrations
        Task AddReignRegistrationAsync(ReignRegistration reg);
        Task<List<ReignRegistration>> GetAllReignRegistrationsAsync();
        Task ClearReignRegistrationsAsync();

        // Update the entire reign list (Needed for removals)
        Task SetReignRegistrationsAsync(List<ReignRegistration> registrations);

        Task<bool> GetReignLockedAsync();
        Task SetReignLockedAsync(bool locked);


        // Donations
        Task AddDonationAsync(DonationRecord record);
        Task<List<DonationRecord>> GetDonationsForWeekAsync(DateTime weekStartUtc);

        // Fines 
        Task AddFineAsync(FineRecord fine);
        Task<List<FineRecord>> GetAllFinesAsync();
        Task UpdateFineAsync(FineRecord fine);
        Task RemoveFineByIdAsync(string fineId);


        // Polls
        Task AddPollAsync(PollRecord poll);
        Task<List<PollRecord>> GetAllPollsAsync();
        Task RemovePollAsync(string pollId);

        // Poll Votes
        Task AddPollVoteAsync(PollVoteRecord vote);
        Task<List<PollVoteRecord>> GetVotesForPollAsync(string pollId);
        Task RemoveVotesForPollAsync(string pollId);

        Task RemoveVoteAsync(string pollId, string discordUserId);
        Task<PollVoteRecord> GetVoteAsync(string pollId, string discordUserId);

        //Scheduled events 
        Task AddScheduledEventAsync(ScheduledEvent evt);
        Task<List<ScheduledEvent>> GetAllScheduledEventsAsync();
        Task UpdateScheduledEventAsync(ScheduledEvent evt);


    }
}
