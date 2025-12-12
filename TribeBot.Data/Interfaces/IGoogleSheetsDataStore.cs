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
        // ==========================================================================
        // MEMBERS
        // ==========================================================================

        // Retrieve a single member by Discord user ID
        Task<Member?> GetMemberAsync(string discordUserId);

        // Retrieve all registered members
        Task<List<Member>> GetAllMembersAsync();

        // Create or update a member record
        Task SaveMemberAsync(Member member);

        // Remove a member using their Discord user ID
        Task<bool> RemoveMemberByDiscordIdAsync(string discordId);


        // ==========================================================================
        // REIGN REGISTRATIONS
        // ==========================================================================

        // Add a new reign registration entry
        Task AddReignRegistrationAsync(ReignRegistration reg);

        // Retrieve all reign registrations
        Task<List<ReignRegistration>> GetAllReignRegistrationsAsync();

        // Remove all reign registrations
        Task ClearReignRegistrationsAsync();

        // Replace the entire reign registration list (used for removals/reordering)
        Task SetReignRegistrationsAsync(List<ReignRegistration> registrations);

        // Retrieve the reign lock state
        Task<bool> GetReignLockedAsync();

        // Set the reign lock state
        Task SetReignLockedAsync(bool locked);


        // ==========================================================================
        // DONATIONS
        // ==========================================================================

        // Add a donation record
        Task AddDonationAsync(DonationRecord record);

        // Retrieve all donations for a specific week (UTC start)
        Task<List<DonationRecord>> GetDonationsForWeekAsync(DateTime weekStartUtc);


        // ==========================================================================
        // FINES
        // ==========================================================================

        // Add a fine record
        Task AddFineAsync(FineRecord fine);

        // Retrieve all fine records
        Task<List<FineRecord>> GetAllFinesAsync();

        // Update an existing fine record
        Task UpdateFineAsync(FineRecord fine);

        // Remove a fine by its unique ID
        Task RemoveFineByIdAsync(string fineId);


        // ==========================================================================
        // POLLS
        // ==========================================================================

        // Add a new poll
        Task AddPollAsync(PollRecord poll);

        // Retrieve all polls
        Task<List<PollRecord>> GetAllPollsAsync();

        // Remove a poll by ID
        Task RemovePollAsync(string pollId);


        // ==========================================================================
        // POLL VOTES
        // ==========================================================================

        // Add a vote to a poll
        Task AddPollVoteAsync(PollVoteRecord vote);

        // Retrieve all votes for a specific poll
        Task<List<PollVoteRecord>> GetVotesForPollAsync(string pollId);

        // Remove all votes associated with a poll
        Task RemoveVotesForPollAsync(string pollId);

        // Remove a specific user's vote from a poll
        Task RemoveVoteAsync(string pollId, string discordUserId);

        // Retrieve a specific user's vote for a poll
        Task<PollVoteRecord> GetVoteAsync(string pollId, string discordUserId);


        // ==========================================================================
        // SCHEDULED EVENTS
        // ==========================================================================

        // Add a scheduled event
        Task AddScheduledEventAsync(ScheduledEvent evt);

        // Retrieve all scheduled events
        Task<List<ScheduledEvent>> GetAllScheduledEventsAsync();

        // Update an existing scheduled event
        Task UpdateScheduledEventAsync(ScheduledEvent evt);


        // ==========================================================================
        // TITLE QUEUE SYSTEM
        // ==========================================================================

        // Add a user to a title queue ("Tycoon" or "Priest")
        Task AddTitleApplicantAsync(string title, string discordUserId);

        // Remove a user from any title queue
        Task RemoveTitleApplicantAsync(string discordUserId);

        // Retrieve the full queue for a given title (ordered by AppliedUtc)
        Task<List<TitleApplicant>> GetTitleQueueAsync(string title);

        // Retrieve all title applicants (used for internal delete-by-ID logic)
        Task<List<TitleApplicant>> GetAllTitleApplicantsAsync();


        // ==========================================================================
        // TITLE ROTATION TIMERS
        // ==========================================================================

        // Retrieve the next rotation timestamp (UTC) for a title
        Task<string?> GetNextTitleRotationUtcAsync(string title);

        // Set the next rotation timestamp (UTC) for a title
        Task SetNextTitleRotationUtcAsync(string title, string utcTimestamp);

        // Retrieve the last awarded user ID (cooldown enforcement)
        Task<string?> GetLastAwardedUserIdAsync(string title);

        // Store the last awarded user ID (cooldown enforcement)
        Task SetLastAwardedUserIdAsync(string title, string discordUserId);
    }
}
