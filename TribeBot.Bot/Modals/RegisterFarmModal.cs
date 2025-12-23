using Discord.Interactions;

namespace TribeBot.Bot.Modals
{
    public class RegisterFarmModal : IModal
    {
        public string Title => "Register Farm";

        [InputLabel("Farm Name")]
        [ModalTextInput("farm_name")]
        public string FarmName { get; set; } = "";

        [InputLabel("Farm Ingame ID")]
        [ModalTextInput("farm_id")]
        public string FarmId { get; set; } = "";
    }
}
