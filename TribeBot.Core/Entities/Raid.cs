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

        public RaidType RaidType { get; set; }

        public DateTime StartUtc { get; set; }

        public ulong ChannelId { get; set; }

        public ulong MessageId { get; set; }

        public bool IsClosed { get; set; }
    }
}
