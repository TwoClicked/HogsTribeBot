namespace TribeBot.Core.Entities
{
    public class DonationRecord
    {
        public string DiscordUserId { get; set; } = "";
        public string IngameName { get; set; } = "";
        public int Amount { get; set; }
        public DateTime TimestampUtc { get; set; }

        // For weekly checks
        public DateTime WeekStartUtc { get; set; }
        public DateTime WeekEndUtc { get; set; }
    }
}
