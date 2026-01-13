using Discord;
using Discord.Interactions;

namespace TribeBot.Bot.Modals
{
    public class KvKCreateModal : IModal
    {
        public string Title => "Create KvK Event";

        [InputLabel("KvK Name")]
        [ModalTextInput(
            "kvk_name",
            TextInputStyle.Short,
            placeholder: "Kingdom vs Kingdom - Season X",
            maxLength: 100)]
        public string Name { get; set; } = "";

        [InputLabel("Start (UTC) — yyyy-MM-dd HH:mm")]
        [ModalTextInput(
            "start_utc",
            TextInputStyle.Short,
            placeholder: "2026-02-01 18:00")]
        public string StartUtc { get; set; } = "";

        [InputLabel("End (UTC) — yyyy-MM-dd HH:mm")]
        [ModalTextInput(
            "end_utc",
            TextInputStyle.Short,
            placeholder: "2026-03-17 18:00")]
        public string EndUtc { get; set; } = "";
    }
}
