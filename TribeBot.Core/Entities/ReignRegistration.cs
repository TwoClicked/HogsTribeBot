using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TribeBot.Core.Entities
{
    public class ReignRegistration
    {

        public string DiscordUserId { get; set; } = "";
        public string IngameName { get; set; } = "";
        public DateTime AppliedAtUtc { get; set; }

    }
}
