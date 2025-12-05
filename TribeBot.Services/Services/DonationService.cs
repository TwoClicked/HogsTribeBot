using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TribeBot.Core.Entities;
using TribeBot.Data.Interfaces;
using TribeBot.Core.Interfaces;

namespace TribeBot.Services.Services
{
    public class DonationService : IDonationService
    {
        private readonly IGoogleSheetsDataStore _dataStore;
        private readonly IMemberService _memberService;

        public DonationService(IGoogleSheetsDataStore dataStore, IMemberService memberService)
        {
            _dataStore = dataStore;
            _memberService = memberService;
        }

        public Task AddDonationAsync(DonationRecord record)
        {
            return _dataStore.AddDonationAsync(record);
        }

        public Task<List<DonationRecord>> GetDonationsForCurrentWeekAsync()
        {
            var now = DateTime.UtcNow;

            // Week starts Monday 00:00 UTC
            int daysSinceMonday = ((int)now.DayOfWeek + 6) % 7;
            DateTime weekStart = now.Date.AddDays(-daysSinceMonday);
            DateTime weekEnd = weekStart.AddDays(7);

            return _dataStore.GetDonationsForWeekAsync(weekStart);
        }

        public async Task<List<Member>> GetMembersMissingDonationsAsync()
        {
            var members = await _memberService.GetAllMembersAsync();
            var donations = await GetDonationsForCurrentWeekAsync();

            var donatedIds = donations.Select(d => d.DiscordUserId).ToHashSet();

            return members
                .Where (m => !m.IsExempt) // Those who are put on TRUE in the sheet will not be pulled forward
                .Where(m => !donatedIds.Contains(m.DiscordUserId))
                .ToList();
        }
        public async Task<int> GetTotalForUserThisWeekAsync(string discordUserId)
        {
            var now = DateTime.UtcNow;
            int daysSinceMonday = ((int)now.DayOfWeek + 6) % 7;
            DateTime weekStart = now.Date.AddDays(-daysSinceMonday);

            var donations = await _dataStore.GetDonationsForWeekAsync(weekStart);

            return donations
                .Where(d => d.DiscordUserId == discordUserId)
                .Sum(d => d.Amount);
        }

        public async Task<Dictionary<string, int>> GetTotalsForAllUsersThisWeekAsync()
        {
            var now = DateTime.UtcNow;
            int daysSinceMonday = ((int)now.DayOfWeek + 6) % 7;
            DateTime weekStart = now.Date.AddDays(-daysSinceMonday);

            var donations = await _dataStore.GetDonationsForWeekAsync(weekStart);

            return donations
                .GroupBy(d => d.DiscordUserId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
        }

    }
}
