using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TribeBot.Core.Entities
{
    public class PollRecord
    {

        public string PollId { get; set; } = "";
        public string Question { get; set; } = "";
        public DateTime EndDateUtc { get; set; }
        public List<string> Options { get; set; } = new();
        public string CreatedByDiscordId { get; set; } = "";
        public DateTime CreatedAtUtc { get; set; }

    }
}
