using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord.Interactions;

namespace TribeBot.Bot.Modals
{

    public class AddKvKEventModal : IModal
    {
        public string Title => "Add KvK Event";

        [InputLabel("KvK ID")]
        [ModalTextInput("kvk_id")]
        public string KvKId { get; set; } = string.Empty;

        [InputLabel("Event Type (gate / killingfield)")]
        [ModalTextInput("event_type")]
        public string EventType { get; set; } = string.Empty;

        [InputLabel("Start Time (UTC yyyy-MM-dd HH:mm)")]
        [ModalTextInput("start_time")]
        public string StartTime { get; set; } = string.Empty;
    }
}
