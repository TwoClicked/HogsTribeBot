using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TribeBot.Core.Entities
{
    public class KvKTimedEvent
    {
        public string EventId { get; set; } = "";
        public string KvKId { get; set; } = "";
        public string EventType { get; set; } = ""; // "KillingField" or "GateOpening"
        public DateTime StartUtc { get; set; }
        public bool AnnouncementSent { get; set; }
    }

}
