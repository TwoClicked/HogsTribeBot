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
            string FormatUsers(IEnumerable<ulong> userIds)
                => userIds.Any()
                    ? string.Join("\n", userIds.Select(id => $"• <@{id}>"))
                    : "_No signups yet_";

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

        private static long ToUnix(DateTime utc)
            => new DateTimeOffset(utc).ToUnixTimeSeconds();
    }
}
