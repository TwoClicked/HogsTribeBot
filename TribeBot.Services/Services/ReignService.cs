using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TribeBot.Core.Entities;
using TribeBot.Core.Interfaces;
using TribeBot.Data.Interfaces;

namespace TribeBot.Services.Services
{
    public class ReignService : IReignService
    {
        private readonly IGoogleSheetsDataStore _dataStore;
        private readonly IMemberService _memberService;

        public ReignService(
            IGoogleSheetsDataStore dataStore,
            IMemberService memberService)
        {
            _dataStore = dataStore;
            _memberService = memberService;
        }

        // ============================================================
        // APPLY FOR REIGN
        // ============================================================

        public async Task ApplyAsync(string discordUserId)
        {
            var member = await _memberService.GetMemberByDiscordIdAsync(discordUserId);

            if (member == null)
                throw new Exception("User is not registered.");

            // Load existing reign applications
            var existing = await _dataStore.GetAllReignRegistrationsAsync();

            // Already applied?
            if (existing.Any(r => r.DiscordUserId == discordUserId))
                throw new Exception("You already applied — no need to do it again!");

            var reg = new ReignRegistration
            {
                DiscordUserId = discordUserId,
                IngameName = member.IngameName,
                AppliedAtUtc = DateTime.UtcNow
            };

            await _dataStore.AddReignRegistrationAsync(reg);
        }

        // ============================================================
        // LIST REIGN APPLICANTS (Sorted by Reign Points)
        // ============================================================

        public async Task<List<(Member member, ReignRegistration registration)>>
            GetCurrentRegistrationsSortedAsync()
        {
            var registrations = await _dataStore.GetAllReignRegistrationsAsync();
            var allMembers = await _memberService.GetAllMembersAsync();

            var joined =
                from reg in registrations
                join m in allMembers on reg.DiscordUserId equals m.DiscordUserId
                orderby m.ReignPoints descending
                select (m, reg);

            return joined.ToList();
        }

        // ============================================================
        // CLEAR REIGN LIST
        // ============================================================

        public Task ClearAsync()
        {
            return _dataStore.ClearReignRegistrationsAsync();
        }

        // ============================================================
        // REIGN LOCK STATE — Option B (Always READ from Google Sheets)
        // ============================================================

        public Task<bool> GetReignLockedAsync()
        {
            // Always read live state from Google Sheets
            return _dataStore.GetReignLockedAsync();
        }

        public Task SetReignLockedAsync(bool locked)
        {
            // Save to Google Sheets (no caching)
            return _dataStore.SetReignLockedAsync(locked);
        }
    }
}
