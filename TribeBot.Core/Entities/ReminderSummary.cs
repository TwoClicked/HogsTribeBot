using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TribeBot.Core.Entities
{
    public class ReminderSummary
    {
        public int TotalMembers { get; set; }
        public int RegisteredMembers { get; set; }
        public int UnregisteredMembers { get; set; }
        public int DMsSent { get; set; }
        public List<ulong> DMFailures { get; set; } = new();
    }
}
