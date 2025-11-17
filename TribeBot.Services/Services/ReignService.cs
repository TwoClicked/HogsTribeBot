using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TribeBot.Core.Entities;
using TribeBot.Data.Interfaces;
using TribeBot.Services.Interfaces;

namespace TribeBot.Services.Services
{
    public class ReignService : IReignService
    {

        private readonly IGoogleSheetsDataStore _dataStore;
        private readonly IMemberService _memberService;


        public ReignService(IGoogleSheetsDataStore dataStore, IMemberService memberService)
        {

            _dataStore = dataStore;
            _memberService = memberService;
        }

        public async Task ApplyAsync(string discordUserId)
        {
            var member = await _memberService.GetMemberByDiscordIdAsync(discordUserId);

            if (member == null)
                throw new Exception("User is not registered.");

            // load exisiting reign applications
            var existing = await _dataStore.GetAllReignRegistrationsAsync();

            //already applied? 
            if (existing.Any(r => r.DiscordUserId == discordUserId))
                throw new Exception("Don't double-date us! You're already registered");


            var reg = new ReignRegistration
            {
                DiscordUserId = discordUserId,
                IngameName = member.IngameName,
                AppliedAtUtc = DateTime.UtcNow
            };

            await _dataStore.AddReignRegistrationAsync(reg);
        }

        public async Task<List<(Member member, ReignRegistration registration)>> GetCurrentRegistrationsSortedAsync()
        {
            var registrations = await _dataStore.GetAllReignRegistrationsAsync();
            var allMembers = await _memberService.GetAllMembersAsync();

            var joined = from reg in registrations
                         join m in allMembers on reg.DiscordUserId equals m.DiscordUserId
                         orderby m.ReignPoints descending 
                         select (m, reg);

            return joined.ToList();
        }

        public Task ClearAsync()
        {
            return _dataStore.ClearReignRegistrationsAsync();
        }


    }
}
