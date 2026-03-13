using Discord;

namespace TribeBot.Bot.UI
{
    public static class HelpEmbeds
    {
        // GENERAL
        public static Embed General() => new EmbedBuilder()
            .WithTitle("📘 General Commands")
            .AddField("Players",
                "`!myinfo` — View your HOGS profile\n" +
                "`!listmembers` — List all registered tribe members")
            .AddField("Officer Only",
                "`!viewinfo @user`")
            .WithColor(Color.Blue)
            .Build();

        // REGISTRATION
        public static Embed Registration() => new EmbedBuilder()
            .WithTitle("🟦 Registration Commands")
            .AddField("Players",
                "`!register` — Begin DM registration")
            .AddField("Officer Only",
                "`!registerreminder`\n" +
                "`!listnonregistered`\n" +
                "`!removemember @user`")
            .WithColor(Color.Blue)
            .Build();

        // PROFILE UPDATE
        public static Embed Update() => new EmbedBuilder()
            .WithTitle("✏️ Profile Update")
            .AddField("Players",
                "`/updateprofile` — Update your profile using a clean form")
            .WithColor(Color.Gold)
            .Build();

        // REIGN
        public static Embed Reign() => new EmbedBuilder()
            .WithTitle("⚔️ Reign Commands")
            .AddField("Players",
                "`!applyreign` — Apply for Viking Reign\n" +
                "`!listreign` — Show sorted applicants\n" +
                "`!leavereign` — Leave the current reign list")
            .AddField("Officer Only",
                "`!clearreign`\n" +
                "`!setreignpoints`\n" +
                "`!SetReignPlayer`\n" +
                "`!lockreign`\n" +
                "`!unlockreign`\n" +
                "`!exempt`\n" +
                "`!unexempt`\n" +
                "`!removereign`")
            .WithColor(Color.DarkRed)
            .Build();

        // BANK
        public static Embed Bank() => new EmbedBuilder()
            .WithTitle("💰 Bank Commands")
            .AddField("Players",
                "`!checkbank` — Check your weekly donation status")
            .AddField("Officer Only",
                "`!bankunpaid` — Show unpaid members\n" +
                "`!payfor` — Pay donations for someone else\n" +
                "`!bankreminder` — Send donation reminders\n")
            .WithColor(Color.Green)
            .Build();

        // FINES
        public static Embed Fines() => new EmbedBuilder()
            .WithTitle("💀 Fine Commands")
            .AddField("Players",
                "`!myfines` — View your fines")
            .AddField("Officer Only",
                "`!fineuser` — Issue an fine\n" +
                "`!finereign` — Issue a reign fine\n" +
                "`!finelist` — View all fines (full list)\n" +
                "`!unpaidfines` — View unpaid fines grouped by type\n" +
                "`!removefine` — Remove a fine by ID\n" +
                "`!unpaidfines` — Show all unpaid fines, sorted\n" +
                "`!verifiedpayment` — Mark fines as paid")
            .WithColor(Color.DarkGrey)
            .Build();

        // POLLS
        public static Embed Polls() => new EmbedBuilder()
            .WithTitle("📊 Poll Commands")
            .AddField("Players",
                "`!polllist`\n" +
                "`!pollshow`\n" +
                "`!vote` (DM only)")
            .AddField("Officer Only",
                "`!pollcreate`\n" +
                "`!pollremove`\n" +
                "`!pollofficer`")
            .WithColor(Color.Purple)
            .Build();

        // CREATOR
        public static Embed Creator() => new EmbedBuilder()
            .WithTitle("🎥 Content Creator Commands")
            .AddField("Players",
                "`!promote` — Send your YouTube video to the promotion channel")
            .WithColor(Color.Magenta)
            .Build();

        // TITLES
        public static Embed Titles() => new EmbedBuilder()
            .WithTitle("🎩 Title System Commands (SERVER ONLY)")
            .AddField("Players",
                "`/applytitle` — Apply for Tycoon or Priest\n" +
                "`/withdrawtitle` — Withdraw from a title queue\n" +
                "`/titlequeue` — View title queues")
            .AddField("Officer Only",
                "`/titlegrant @user <title>` — Grant title and advance rotation")
            .WithColor(Color.Blue)
            .Build();

        // SWORN VENGEANCE
        public static Embed Sworn() => new EmbedBuilder()
            .WithTitle("⚔️ Sworn Vengeance Commands")
            .AddField("Officers",
                "`/hesworn` — Notify HogsEvents members next Sworn level unlocked\n" +
                "`/heswornfinal` — Notify entire HOGS role FINAL Sworn level")
            .WithColor(Color.Red)
            .Build();

        // EVENTS
        public static Embed Events() => new EmbedBuilder()
            .WithTitle("📅 Event Scheduling Commands")
            .AddField("Officers",
                "`/hevent` — Schedule a tribe event\n" +
                "`/helist` — View scheduled events\n" +
                "`/heedit` — Edit a scheduled event\n" +
                "`/hedelete` — Delete a scheduled event")
            .WithColor(Color.Orange)
            .Build();

        // FARMS
        public static Embed Farms() => new EmbedBuilder()
            .WithTitle("🌾 Farm Commands")
            .AddField("Players",
                "`/farm add` — Register a single farm\n" +
                "`/farm bulk` — Register multiple farms\n" +
                "`/farm list` — Receive a DM with your farms\n" +
                "`/farm edit <farmId>` — Edit a farm you own\n" +
                "`/farm remove <farmId>` — Remove a farm you own")
            .AddField("Notes",
                "• Farm IDs must be numeric\n" +
                "• Duplicate IDs are blocked\n" +
                "• Ownership is enforced")
            .WithColor(Color.Green)
            .Build();

        // FARM TRIBES
        public static Embed FarmTribes() => new EmbedBuilder()
            .WithTitle("👥 Farm Tribe Commands")
            .AddField("Players",
                "`/farmtribe research` — Notify officers research completed\n" +
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
                "• Assignment required for notifications\n" +
                "• Unassigned players may still register farms")
            .WithColor(Color.DarkGreen)
            .Build();

        // RAID SIGNUPS
        public static Embed Raids() => new EmbedBuilder()
            .WithTitle("⚔️ Raid Signup Commands")
            .AddField("Officers",
                "`/raid create` — Create a raid signup using a modal\n" +
                "`/raid delete <raidId>` — Delete a raid signup\n" +
                "`/raid list` — List active raids")
            .AddField("Players",
                "Use buttons on raid posts to respond:\n" +
                "• ✅ Yes\n" +
                "• ❌ No\n" +
                "• ❔ Maybe")
            .WithColor(Color.DarkRed)
            .Build();
    }
}
