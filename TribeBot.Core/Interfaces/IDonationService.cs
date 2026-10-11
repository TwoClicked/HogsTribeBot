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

        // Week starts (Monday 00:00 UTC) this member already has a donation for
        Task<List<DateTime>> GetPaidWeekStartsForUserAsync(string discordUserId);

        // The next `weeks` unpaid week starts, beginning at the current week
        Task<List<DateTime>> GetNextUnpaidWeekStartsAsync(string discordUserId, int weeks);

        // Writes one donation row per unpaid week; returns the week starts that were covered
        Task<List<DateTime>> AddPrepaidWeeksAsync(Member member, int weeks, int amountPerWeek, DateTime timestampUtc);

    }
}
