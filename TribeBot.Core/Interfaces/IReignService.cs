using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TribeBot.Core.Entities;

namespace TribeBot.Core.Interfaces
{
    public interface IReignService
    {
        // Reign Applications
        Task ApplyAsync(string discordUserId);
        Task<List<(Member member, ReignRegistration registration)>> GetCurrentRegistrationsSortedAsync();
        Task ClearAsync();

        // Lock state
        Task<bool> GetReignLockedAsync();
        Task SetReignLockedAsync(bool locked);

        // NEW: Remove member from reign (Officer or User)
        Task<bool> RemoveMemberFromReignAsync(string discordUserId);
    }
}
