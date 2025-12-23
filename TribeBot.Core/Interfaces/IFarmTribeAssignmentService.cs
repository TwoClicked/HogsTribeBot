using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TribeBot.Core.Entities;

namespace TribeBot.Core.Interfaces
{
    public interface IFarmTribeAssignmentService
    {
        Task AssignPlayerAsync(string discordUserId, string farmTribeId);
        Task RemovePlayerAsync(string discordUserId);
        Task<PlayerFarmTribeAssignment?> GetAssignmentForUserAsync(string discordUserId);
        Task<List<PlayerFarmTribeAssignment>> GetAssignmentsForTribeAsync(string farmTribeId);
    }
}
