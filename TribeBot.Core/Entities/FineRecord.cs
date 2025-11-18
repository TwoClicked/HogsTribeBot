using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TribeBot.Core.Entities
{
    public class FineRecord
    {

        public string FineId { get; set; } = ""; // GUID per fine
        public string DiscordUserId { get; set; } = ""; 
        public string IngameName { get; set; } = "";


        public int Amount { get; set; }  //Fine amount 
        public string FineType { get; set; } = "";  // "Event" or "Reign"

        public bool IsPaid { get; set; } // Officer can find a list of the people who have paid fines and then remove the paid fines if confirmed and verified 
        public int PaidAmount { get; set; } // Running total of OCR payments 

        public int ReignStrikes { get; set; } // Only used for the blacklist in the viking reign 
        public string Notes { get; set; } = ""; //Reason /rss type / description 

        public DateTime IssuedAtUtc { get; set; } // When fine was created

    }
}
