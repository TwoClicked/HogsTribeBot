using Discord;

namespace TribeBot.Bot.UI
{
    public static class HelpEmbeds
    {
        public static Embed General() => new EmbedBuilder()
            .WithTitle("📘 General Commands")
            .AddField("/checkbot", "Check if bot is online")
            .AddField("!myinfo", "View your profile")
            .AddField("!listmembers", "List all members")
            .AddField("Officer Only", "`!viewinfo @user`")
            .WithColor(Color.Blue)
            .Build();

        public static Embed Registration() => new EmbedBuilder()
            .WithTitle("🟦 Registration Commands")
            .AddField("!register", "Begin DM registration")
            .AddField("Officer Only", "`!registerreminder`, `!listnonregistered`, `!removemember @user`")
            .WithColor(Color.Blue)
            .Build();

        public static Embed Update() => new EmbedBuilder()
            .WithTitle("✏️ Update Commands (After the command make sure to add the value)")
            .AddField("!updateigname", "Update in-game name")
            .AddField("!updateid", "Update in-game ID")
            .AddField("!updatemight", "Update Might")
            .AddField("!updatekills", "Update Kill Points")
            .AddField("!updatecollector", "Update collector level")
            .AddField("Officer Only", "`!updateReignPoints @user points`")
            .WithColor(Color.Gold)
            .Build();

        public static Embed Reign() => new EmbedBuilder()
            .WithTitle("⚔️ Reign Commands")
            .AddField("!applyreign", "Apply for Viking Reign")
            .AddField("!listreign", "Show sorted applicants")
            .AddField("Officer Only", "`!clearreign`, `!lockreign`, `!unlockreign`, `!exempt`, `!unexempt`")
            .WithColor(Color.DarkRed)
            .Build();

        public static Embed Bank() => new EmbedBuilder()
            .WithTitle("💰 Bank Commands")
            .AddField("!bankunpaid", "Show unpaid members")
            .AddField("!checkbank", "Check your donation progress")
            .AddField("!payfor", "Pay for someone else")
            .AddField("Officer Only", "`!bankreminder`")
            .WithColor(Color.Green)
            .Build();

        public static Embed Fines() => new EmbedBuilder()
            .WithTitle("💀 Fine Commands")
            .AddField("!myfines", "View your fines")
            .AddField("Officer Only", "`!fineuser`, `!finereign`, `!finelist`, `!removefine`")
            .WithColor(Color.DarkGrey)
            .Build();

        public static Embed Polls() => new EmbedBuilder()
            .WithTitle("📊 Poll Commands")
            .AddField("User", "`!polllist`, `!pollshow`, `!vote` (DM only)")
            .AddField("Officer", "`!pollcreate`, `!pollremove`, `!pollofficer`")
            .WithColor(Color.Purple)
            .Build();

        public static Embed Creator() => new EmbedBuilder()
            .WithTitle("🎥 Content Creator Commands")
            .AddField("!promote", "Send your YouTube video to the promotion channel")
            .WithColor(Color.Magenta)
            .Build();
    }
}
