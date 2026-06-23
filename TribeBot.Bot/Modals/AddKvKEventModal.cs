using Discord;
using Discord.Interactions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TribeBot.Bot.Modals
{
    public class AddKvKEventModal : IModal
    {
        public string Title => "Add KvK Event";

        [InputLabel("KvK ID")]
        [ModalTextInput("kvk_id")]
        public string KvKId { get; set; } = string.Empty;

        [InputLabel("Event Type")]
        [ModalTextInput("event_type")]
        public string EventType { get; set; } = string.Empty;

        [InputLabel("Start Time (UTC yyyy-MM-dd HH:mm)")]
        [ModalTextInput("start_time")]
        public string StartTime { get; set; } = string.Empty;

        [InputLabel("Description (optional, supports @role mentions)")]
        [ModalTextInput("description", TextInputStyle.Paragraph, minLength: 0, maxLength: 300)]
        public string Description { get; set; } = string.Empty;
    }
}