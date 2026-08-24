using Discord;
using Discord.Interactions;

namespace TribeBot.Bot.Modals
{
    public class MightCutModal : IModal
    {
        public string Title => "Might Cut Message";

        [ModalTextInput("mightcut_message", TextInputStyle.Paragraph)]
        public string Message { get; set; }

        [ModalTextInput("target_user_id")]
        public string TargetUserId { get; set; }   // hidden identifier, pre-filled, do not edit
    }
}