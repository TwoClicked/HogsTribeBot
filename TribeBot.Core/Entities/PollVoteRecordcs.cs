using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TribeBot.Core.Entities
{
    public class PollVoteRecord
    {
        public string PollId { get; set; } = "";
        public string DiscordUserId { get; set; } = "";
        public string IngameName { get; set; } = "";
        public string Choice { get; set; } = "";
        public DateTime TimestampUtc { get; set; }
    }
}
