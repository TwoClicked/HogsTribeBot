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
        public string EventType { get; set; } = "";
        public DateTime StartUtc { get; set; }
        public bool AnnouncementSent { get; set; }
        public string Description { get; set; } = "";
    }

}
