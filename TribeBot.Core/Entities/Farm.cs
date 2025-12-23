using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TribeBot.Core.Entities
{
    public class Farm
    {
        public string FarmId { get; set; } = "";
        public string FarmName { get; set; } = ""; 
        public string OwnerDiscordId { get; set; } = "";
        public string OwnerIngameName { get; set; } = "";
        public DateTime RegisteredUtc { get; set; }
    }
}
