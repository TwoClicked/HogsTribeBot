namespace TribeBot.Bot.UI
{
    public static class BankPrepayIds
    {
        public const ulong ChannelId = 1558926789240291379;

        public const int MaxWeeksPerRequest = 5;

        public const int FoodWoodStonePerWeek = 12_000_000;
        public const int GoldPerWeek = 8_000_000;

        // Written in the Donations Amount column for each prepaid week (3 × 12M + 8M)
        public const int PrepaidWeekValue = (FoodWoodStonePerWeek * 3) + GoldPerWeek;

        // Custom ID format: {prefix}:{memberDiscordId}:{weeks}:{uploadMessageId}
        public const string Approve = "bankprepay_approve";
        public const string Deny = "bankprepay_deny";

        // Custom ID format: {prefix}:{memberDiscordId}:{weeks}:{uploadMessageId}:{requestMessageId}
        public const string DenyModal = "bankprepay_denymodal";
    }
}