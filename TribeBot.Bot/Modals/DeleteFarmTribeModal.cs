using Discord.Interactions;

namespace TribeBot.Bot.Modals
{
    public class DeleteFarmTribeModal : IModal
    {
        public string Title => "Confirm Farm Tribe Deletion";

        [InputLabel("Type DELETE to confirm")]
        [ModalTextInput("confirm_text")]
        public string Confirmation { get; set; } = "";
    }
}
