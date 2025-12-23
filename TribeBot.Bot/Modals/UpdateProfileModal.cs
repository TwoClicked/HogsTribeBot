using Discord.Interactions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TribeBot.Bot.Modals
{
    public class UpdateProfileModal : IModal
    {
        public string Title => "Update Your HOGS Profile";

        [InputLabel("In-Game Name")]
        [ModalTextInput("ign", placeholder: "Your character name")]
        public string IngameName { get; set; }

        [InputLabel("In-Game ID")]
        [ModalTextInput("id", placeholder: "Numeric ID")]
        public string IngameId { get; set; }

        [InputLabel("Might")]
        [ModalTextInput("might")]
        public string Might { get; set; }

        [InputLabel("Kill Points")]
        [ModalTextInput("kills")]
        public string Kills { get; set; }

        [InputLabel("Collector Level")]
        [ModalTextInput("collector")]
        public string Collector { get; set; }
    }
}
