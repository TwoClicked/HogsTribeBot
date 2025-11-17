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

        // Reign registrations
        Task AddReignRegistrationAsync(ReignRegistration reg);
        Task<List<ReignRegistration>> GetAllReignRegistrationsAsync();
        Task ClearReignRegistrationsAsync();

        // Donations
        Task AddDonationAsync(DonationRecord record);
        Task<List<DonationRecord>> GetDonationsForWeekAsync(DateTime weekStartUtc);
    }
}
