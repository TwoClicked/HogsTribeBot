using Discord;

namespace TribeBot.Bot.UI
{
    public static class HelpEmbeds
    {
        // GENERAL
        public static Embed General() => new EmbedBuilder()
            .WithTitle("📘 General Commands")
            .AddField("!myinfo", "View your HOGS profile")
            .AddField("!listmembers", "List all registered tribe members")
            .AddField("Officer Only", "`!viewinfo @user`")
            .WithColor(Color.Blue)
            .Build();

        // REGISTRATION
        public static Embed Registration() => new EmbedBuilder()
            .WithTitle("🟦 Registration Commands")
            .AddField("!register", "Begin DM registration")
            .AddField("Officer Only", "`!registerreminder`, `!listnonregistered`, `!removemember @user`")
            .WithColor(Color.Blue)
            .Build();

        // PROFILE UPDATE
        public static Embed Update() => new EmbedBuilder()
            .WithTitle("✏️ Update Command")
            .AddField("/updateprofile", "Update your profile using a clean form")
            .WithColor(Color.Gold)
            .Build();

        // REIGN
        public static Embed Reign() => new EmbedBuilder()
            .WithTitle("⚔️ Reign Commands")
            .AddField("!applyreign", "Apply for Viking Reign")
            .AddField("!listreign", "Show sorted applicants")
            .AddField("!leavereign", "Leave the current reignlist")
            .AddField("Officer Only", "`!clearreign`, `!lockreign`, `!unlockreign`, `!exempt`, `!unexempt`, `!removereign`")
            .WithColor(Color.DarkRed)
            .Build();

        // BANK
        public static Embed Bank() => new EmbedBuilder()
            .WithTitle("💰 Bank Commands")
            .AddField("!bankunpaid", "Show unpaid members")
            .AddField("!checkbank", "Check your weekly donation progress")
            .AddField("!payfor", "Pay donations for someone else")
            .AddField("Officer Only", "`!bankreminder`")
            .WithColor(Color.Green)
            .Build();

        // FINES
        public static Embed Fines() => new EmbedBuilder()
            .WithTitle("💀 Fine Commands")
            .AddField("!myfines", "View your fines")
            .AddField("Officer Only", "`!fineuser`, `!finereign`, `!finelist`, `!removefine`, `!verifiedpayment`")
            .WithColor(Color.DarkGrey)
            .Build();

        // POLLS
        public static Embed Polls() => new EmbedBuilder()
            .WithTitle("📊 Poll Commands")
            .AddField("User", "`!polllist`, `!pollshow`, `!vote` (DM only)")
            .AddField("Officer", "`!pollcreate`, `!pollremove`, `!pollofficer`")
            .WithColor(Color.Purple)
            .Build();

        // CREATOR
        public static Embed Creator() => new EmbedBuilder()
            .WithTitle("🎥 Content Creator Commands")
            .AddField("!promote", "Send your YouTube video to the promotion channel")
            .WithColor(Color.Magenta)
            .Build();

        // TITLES
        public static Embed Titles() => new EmbedBuilder()
            .WithTitle("🎩 Title System Commands (COMMANDS FOR SERVER NOT DM)")
            .AddField("/applytitle", "Apply for the Tycoon or Priest title")
            .AddField("/withdrawtitle", "Withdraw from a title queue")
            .AddField("/titlequeue", "View the Tycoon & Priest title queues")
            .AddField("Officer Only", "`/titlegrant @user <title>` — Grant a title and advance rotation")
            .WithColor(Color.Blue)
            .Build();

        // SWORN VENGEANCE
        public static Embed Sworn() => new EmbedBuilder()
            .WithTitle("⚔️ Sworn Vengeance Commands")
            .AddField("/hesworn", "Notify HogsEvents members that the next Sworn level is unlocked")
            .AddField("/heswornfinal", "Notify the entire HOGS role that the FINAL Sworn level has begun")
            .WithColor(Color.Red)
            .Build();

        // EVENTS
        public static Embed Events() => new EmbedBuilder()
            .WithTitle("📅 Event Scheduling Commands")
            .AddField("/hevent", "Schedule a tribe event (officers)")
            .AddField("/helist", "View scheduled events (officers)")
            .AddField("/heedit", "Edit a scheduled event (officers)")
            .AddField("/hedelete", "Delete a scheduled event (officers)")
            .WithColor(Color.Orange)
            .Build();

        // FARMS

        public static Embed Farms() => new EmbedBuilder()
           .WithTitle("🌾 Farm Commands")
           .AddField("/farm add", "Register a single farm using a form")
           .AddField("/farm bulk", "Register multiple farms at once (bulk input)")
           .AddField("/farm list", "Receive a DM listing your registered farms")
           .AddField("/farm edit <farmId>", "Edit one of your registered farms")
           .AddField("/farm remove <farmId>", "Remove a farm you own")
           .AddField("Notes",
               "• Farm IDs must be numeric\n" +
               "• Duplicate IDs are blocked\n" +
               "• You can only edit or remove farms you own")
           .WithColor(Color.Green)
           .Build();

        //Farm tribes

        public static Embed FarmTribes() => new EmbedBuilder()
            .WithTitle("👥 Farm Tribe Commands")
            .AddField("Players",
                "`/farmtribe research` — Notify officers research is completed\n" +
                "`/farmtribe goldmine` — Notify officers gold mine expired")
            .AddField("Officers",
                "`/farmtribe register` — Create a farm tribe\n" +
                "`/farmtribe edit <tribeId>` — Edit tribe details\n" +
                "`/farmtribe delete <tribeId>` — Delete a tribe\n" +
                "`/farmtribe assign @user <tribeId>` — Assign player\n" +
                "`/farmtribe unassign @user` — Remove player\n" +
                "`/farmtribe list` — List all farm tribes\n" +
                "`/farmtribe overview` — View all players & farm counts")
            .AddField("Important",
                "• Tribe capacity is enforced automatically\n" +
                "• Players must be assigned to use tribe notifications\n" +
                "• Unassigned players may still register farms")
            .WithColor(Color.DarkGreen)
            .Build();


    }
}
