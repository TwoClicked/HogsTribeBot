using Discord;
using Discord.Interactions;

namespace TribeBot.Bot.Handlers
{
    public class ScheduleEventModal : IModal
    {
        public string Title => "Schedule Event";

        [ModalTextInput("eventname")]
        public string EventName { get; set; }

        [ModalTextInput("eventdate")]
        public string EventDate { get; set; }

        [ModalTextInput("eventtime")]
        public string EventTime { get; set; }

        [ModalTextInput("nowinfo")]
        public string NowInfo { get; set; }

        [ModalTextInput("eventmessage", TextInputStyle.Paragraph)]
        public string CustomMessage { get; set; }
    }

    public class DeleteEventModal : IModal
    {
        public string Title => "Delete Event";

        [ModalTextInput("confirmtext")]
        public string ConfirmText { get; set; }

        [ModalTextInput("eventid")]
        public string EventId { get; set; }    // hidden identifier
    }

    public class EditEventModal : IModal
    {
        public string Title => "Edit Event";

        [ModalTextInput("editname")]
        public string EventName { get; set; }

        [ModalTextInput("editdate")]
        public string EventDate { get; set; }

        [ModalTextInput("edittime")]
        public string EventTime { get; set; }

        [ModalTextInput("editmessage", TextInputStyle.Paragraph)]
        public string Message { get; set; }

        [ModalTextInput("eventid")]
        public string EventId { get; set; }   // hidden identifier
    }
}
