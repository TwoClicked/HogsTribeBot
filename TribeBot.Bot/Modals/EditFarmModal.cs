using Discord.Interactions;

namespace TribeBot.Bot.Modals
{
    public class EditFarmModal : IModal
    {
        public string Title => "Edit Farm";

        [InputLabel("Farm Name")]
        [ModalTextInput("farm_name")]
        public string FarmName { get; set; }

        [InputLabel("Farm ID")]
        [ModalTextInput("farm_id")]
        public string FarmId { get; set; }
    }
}