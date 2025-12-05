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
    public class MemberService : IMemberService
    {

        private readonly IGoogleSheetsDataStore _dataStore;

        public MemberService(IGoogleSheetsDataStore dataStore)
        {

            _dataStore = dataStore;
            
        }

        public Task<Member?> GetMemberByDiscordIdAsync(string discordUserId)
        {
            return _dataStore.GetMemberAsync(discordUserId);
        }

        public Task<List<Member>> GetAllMembersAsync()
        {
            return _dataStore.GetAllMembersAsync();
        }

        public Task RegisterOrUpdateAsync(Member member)
        {
            member.LastUpdatedUTC = DateTime.UtcNow;
            return _dataStore.SaveMemberAsync(member);
        }
    }
}
