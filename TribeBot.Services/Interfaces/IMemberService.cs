using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TribeBot.Core.Entities;

namespace TribeBot.Services.Interfaces
{
    public interface IMemberService
    {
        Task<Member?> GetMemberByDiscordIdAsync(string discordUserId);
        Task<List<Member>> GetAllMembersAsync();
        Task RegisterOrUpdateAsync(Member member);
    }
}
