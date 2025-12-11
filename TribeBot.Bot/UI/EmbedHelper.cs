using Discord;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TribeBot.Bot.UI
{
    public static class EmbedHelper
    {
        // Base builder
        private static Embed Build(string title, string desc, Color color)
        {
            return new EmbedBuilder()
                .WithTitle(title)
                .WithDescription(desc)
                .WithColor(color)
                .WithFooter($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC")
                .Build();
        }

        // =============================
        //  Common Embed Types
        // =============================

        public static Embed Success(string text)
            => Build("🟢 Success", text, Color.Green);

        public static Embed Error(string text)
            => Build("❌ Error", text, Color.Red);

        public static Embed Warning(string text)
            => Build("⚠️ Warning", text, Color.Orange);

        public static Embed Info(string title, string text)
            => Build($"🛡️ {title}", text, Color.Blue);

        // =============================
        //  Officer Log Embeds
        // =============================

        public static Embed Log(string title, Dictionary<string, string> fields)
        {
            var embed = new EmbedBuilder()
                .WithTitle($"📘 {title}")
                .WithColor(new Color(0, 110, 255))
                .WithFooter($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");

            foreach (var pair in fields)
                embed.AddField(pair.Key, pair.Value, true);

            return embed.Build();
        }

        // =============================
        //  Custom Colored Embeds
        // =============================

        public static Embed Custom(string title, string desc, Color color)
            => Build(title, desc, color);
    }
}
