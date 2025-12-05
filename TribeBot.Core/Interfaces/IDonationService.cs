using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TribeBot.Core.Entities;

namespace TribeBot.Core.Interfaces
{
    public interface IDonationService
    {
        Task AddDonationAsync(DonationRecord record);
        Task<List<DonationRecord>> GetDonationsForCurrentWeekAsync();
        Task<List<Member>> GetMembersMissingDonationsAsync();

        Task<int> GetTotalForUserThisWeekAsync(string discordUserId);
        Task<Dictionary<string, int>> GetTotalsForAllUsersThisWeekAsync();

    }
}
