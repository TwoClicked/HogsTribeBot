using Discord.Interactions;

namespace TribeBot.Bot.Modals
{
    public class AddRaidModal : IModal
    {
        public string Title => "Create Raid Signup";

        [ModalTextInput(
            "kvk_id",
            placeholder: "e.g. KVK-12",
            maxLength: 50)]
        public string KvKId { get; set; }

        [ModalTextInput(
            "raid_type",
            placeholder: "gate / killingfield",
            maxLength: 20)]
        public string RaidType { get; set; }

        [ModalTextInput(
            "start_time",
            placeholder: "yyyy-MM-dd HH:mm (UTC)",
            maxLength: 20)]
        public string StartTime { get; set; }
    }
}
