using Discord.Interactions;

namespace TribeBot.Bot.Modals
{
    public class RegisterFarmTribeModal : IModal
    {
        public string Title => "Register Farm Tribe";

        [InputLabel("Farm Tribe Name")]
        [ModalTextInput("farmtribe_name")]
        public string FarmTribeName { get; set; } = "";

        [InputLabel("Total Farm Slots")]
        [ModalTextInput("farmtribe_slots")]
        public string TotalSlots { get; set; } = "";
    }
}
