using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TribeBot.Core.Entities;

namespace TribeBot.Core.Interfaces
{
    public interface IFarmService
    {

        //PLAYER REGISTERS A FARM 
        Task RegisterFarmAsync(string farmId, string farmName, string ownerDiscordId, string ownerIngameName);
        //Get farm per user (DiscordId)
        Task<List<Farm>> GetFarmsForUserAsync(string discordUserId);
        //Get farm by ID
        Task<Farm?> GetFarmByIdAsync(string farmId);
        // PLAYER REMOVES A FARM FROM PROFILE
        Task RemoveFarmAsync(string farmId, string discordUserId);

    }
}
