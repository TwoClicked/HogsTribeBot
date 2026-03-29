using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace TribeBot.Core.Entities
{
    public class Member
    {
        public string DiscordUserId { get; set; } = "";
        public string IngameName { get; set; } = "";
        public string IngameId { get; set; }
        public int Might { get; set; }
        public long KillPoints { get; set; }
        public int CollectorLevel { get; set; }
        public long ReignPoints { get; set; }
        public DateTime LastUpdatedUTC { get; set; }

        // 🔥 NEW (split exemptions)
        public bool BankExempt { get; set; }
        public bool DeliveryExempt { get; set; }
    }
}
