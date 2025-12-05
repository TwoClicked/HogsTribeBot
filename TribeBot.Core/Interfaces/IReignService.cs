using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
    }
}
