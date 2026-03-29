public class DeliveryEntry
{
    public string EventId { get; set; }
    public DateTime StartUtc { get; set; }
    public DateTime? EndUtc { get; set; }

    public string DiscordUserId { get; set; }
    public string IngameName { get; set; }

    public string SubmissionType { get; set; } // Bracelet | Gold
    public int Amount { get; set; }

    public DateTime TimestampUtc { get; set; }
}