using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TribeBot.Core.Entities;

namespace TribeBot.Services.Interfaces
{
    public interface IReignService
    {
        Task ApplyAsync(string discordUserId);
        Task<List<(Member member, ReignRegistration registration)>> GetCurrentRegistrationsSortedAsync();
        Task ClearAsync();
    }
}
