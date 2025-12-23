using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TribeBot.Core.Entities
{
    public class FarmTribe
    {

        //Internal primary key (Never shown but to track) 
        public string FarmTribeId { get; set; } = "";

        // Display name (What officers recognize) 
        public string FarmTribeName { get; set; } = "";

        // Maximum number of farms this tribe can hold 
        public int TotalSlots { get; set; }

        // Currently assigned farms 
        public int UsedSlots { get; set; }

        public DateTime CreatedUtc { get; set; }

    }
}
