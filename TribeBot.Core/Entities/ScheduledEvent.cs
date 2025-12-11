using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TribeBot.Core.Entities
{
    public class ScheduledEvent
    {
        public string EventId { get; set; } = "";
        public string EventName { get; set; } = "";
        public DateTime EventDateUtc { get; set; } 
        public int ReminderOffsetHours { get; set; }
        public string Message { get; set; } = "";
        public string CreatedBy { get; set; } = "";
        public DateTime CreatedAtUtc { get; set; }
        public bool ReminderSent { get; set; }
        public bool Completed { get; set; }
    }
}
