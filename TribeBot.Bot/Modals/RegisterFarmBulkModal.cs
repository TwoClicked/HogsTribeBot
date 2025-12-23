using Discord.Interactions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TribeBot.Bot.Modals
{
    public class RegisterFarmBulkModal : IModal
    {

        public string Title => "Register farms (Bulk)";

        [InputLabel("Farm list **ONE PER LINE:** Name | ID")]
        [ModalTextInput(
            "farm_list",
            Discord.TextInputStyle.Paragraph,
            placeholder:
            @"FarmAlpha | 123456 
              FarmBravo | 654321)]
              Sallyfarm | 1511451",
            maxLength: 4000)]


        public string FarmList { get; set; } = "";

    }
}
