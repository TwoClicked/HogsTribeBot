using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TribeBot.Core.Enums;

namespace TribeBot.Core.Entities
{
    public class RaidSignup
    {
        public string RaidId { get; set; } = null!;

        public ulong UserId { get; set; }

        public RaidSignupResponse Response { get; set; }

        public DateTime UpdatedUtc { get; set; }
    }
}
