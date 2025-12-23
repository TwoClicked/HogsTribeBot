using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TribeBot.Core.Entities
{
    public class PlayerFarmTribeAssignment
    {

        public string DiscordUserId { get; set; } = "";
        public string FarmTribeId { get; set; } = "";
        public DateTime AssignedUtc {  get; set; }

    }
}
