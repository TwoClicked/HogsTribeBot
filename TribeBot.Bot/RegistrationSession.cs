using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TribeBot.Bot
{
    public class RegistrationSession
    {

        public enum Step
        {
            None,
            AskIngameName,
            AskIngameId,
            AskMight,
            AskKillPoints,
            AskCollectorLevel,
            Complete
        }

        public Step CurrentStep {  get; set; } = Step.None;

        public string IngameName { get; set; } = "";
        public string IngameId { get; set; } = "";
        public int Might { get; set; }
        public long KillPoints { get; set; }
        public int CollectorLevel { get; set; }

    }
}
