using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TribeBot.Core.Enums;

namespace TribeBot.Core.Entities
{
    public class Raid
    {
        public string RaidId { get; set; } = Guid.NewGuid().ToString("N");

        public string RaidType { get; set; } = default!; // free-text: "Gate", "Killing Field", "Boss Spawn", etc.

        public DateTime StartUtc { get; set; }

        public ulong ChannelId { get; set; }

        public ulong MessageId { get; set; }

        public bool IsClosed { get; set; }

        public string Description { get; set; } = "";
    }
}