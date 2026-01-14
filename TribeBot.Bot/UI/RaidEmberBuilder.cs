using Discord;
using System;
using System.Collections.Generic;
using System.Linq;
using TribeBot.Core.DTOS;
using TribeBot.Core.Entities;
using TribeBot.Core.Enums;

namespace TribeBot.Bot.UI
{
    public static class RaidEmbedBuilder
    {
        private const int MaxFieldLength = 900;

        // ======================================================
        // INITIAL EMBED (no signups yet)
        // ======================================================
        public static Embed BuildInitial(
            string kvkId,
            RaidType raidType,
            DateTime startUtc)
        {
            var title = raidType == RaidType.Gate
                ? "🚪 Gate Raid Signup"
                : "⚔️ Killing Field Raid Signup";

            return new EmbedBuilder()
                .WithTitle(title)
                .WithColor(raidType == RaidType.Gate ? Color.Gold : Color.Red)
                .WithDescription(
                    $"**KvK ID:** `{kvkId}`\n" +
                    $"**Start Time:** <t:{ToUnix(startUtc)}:F> UTC\n" +
                    $"**Time Remaining:** <t:{ToUnix(startUtc)}:R>")
                .AddField("✅ YES", "_No signups yet_", true)
                .AddField("❔ MAYBE", "_No signups yet_", true)
                .AddField("❌ NO", "_No signups yet_", true)
                .WithFooter("Click a button below to sign up")
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();
        }

        // ======================================================
        // UPDATED EMBED (with signups)
        // ======================================================
        public static Embed BuildWithSignups(
            Raid raid,
            RaidSignupSummary summary)
        {
            int yesCount = summary.Yes.Count();
            int noCount = summary.No.Count();
            int maybeCount = summary.Maybe.Count();

            var title = raid.RaidType == RaidType.Gate
                ? "🚪 Gate Raid Signup"
                : "⚔️ Killing Field Raid Signup";

            return new EmbedBuilder()
                .WithTitle(title)
                .WithColor(raid.RaidType == RaidType.Gate ? Color.Gold : Color.Red)
                .WithDescription(
                    $"**Start Time:** <t:{ToUnix(raid.StartUtc)}:F>\n" +
                    $"**Time Remaining:** <t:{ToUnix(raid.StartUtc)}:R>")
                .AddField($"✅ YES ({yesCount})", FormatUsers(summary.Yes), true)
                .AddField($"❔ MAYBE ({maybeCount})", FormatUsers(summary.Maybe), true)
                .AddField($"❌ NO ({noCount})", FormatUsers(summary.No), true)
                .WithFooter("Click a button below to update your response")
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();
        }

        public static MessageComponent BuildRaidComponents()
        {
            return new ComponentBuilder()
                .WithButton("YES", RaidButtonIds.Yes, ButtonStyle.Success)
                .WithButton("MAYBE", RaidButtonIds.Maybe, ButtonStyle.Secondary)
                .WithButton("NO", RaidButtonIds.No, ButtonStyle.Danger)
                .WithButton("Show Roster", RaidButtonIds.ShowRoster, ButtonStyle.Primary)
                .Build();
        }


        // ======================================================
        // HELPERS
        // ======================================================
        private static string FormatUsers(IEnumerable<ulong> userIds)
        {
            if (!userIds.Any())
                return "_No signups yet_";

            var lines = new List<string>();
            int length = 0;
            int shown = 0;
            int total = userIds.Count();

            foreach (var id in userIds)
            {
                var line = $"• <@{id}>";

                if (length + line.Length + 1 > MaxFieldLength)
                    break;

                lines.Add(line);
                length += line.Length + 1;
                shown++;
            }

            int remaining = total - shown;

            if (remaining > 0)
                lines.Add($"_…and {remaining} more_");

            return string.Join("\n", lines);
        }

        private static long ToUnix(DateTime utc)
            => new DateTimeOffset(utc).ToUnixTimeSeconds();
    }
}
