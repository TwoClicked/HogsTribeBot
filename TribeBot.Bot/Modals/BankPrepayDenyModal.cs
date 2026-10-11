using Discord;
using Discord.Interactions;

namespace TribeBot.Bot.Modals
{
    public class BankPrepayDenyModal : IModal
    {
        public string Title => "Deny Bank Prepay";

        [ModalTextInput("deny_reason", TextInputStyle.Paragraph)]
        public string Reason { get; set; }
    }
}