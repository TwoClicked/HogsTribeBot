using Discord.Interactions;
using System;

namespace TribeBot.Bot.Modals
{
    public class RegisterFarmBulkModal : IModal
    {
        public string Title => "Register farms (Bulk)";

        [InputLabel("Farm list (ONE PER LINE): Name | ID")]
        [ModalTextInput(
                    "farm_list",
                    Discord.TextInputStyle.Paragraph,
                    placeholder:
        @"FarmAlpha | 123456
        FarmBravo | 654321
        Sallyfarm | 1511451
        
        Remove the placeholder and place your info.
        MAX 15 farms per submit or it may crash!",
                    maxLength: 4000)]
        public string FarmList { get; set; } = "";
    }
}