using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TribeBot.Core.Interfaces;
namespace TribeBot.Bot.Hosting
{
    public class KvKAnnouncementWorker
    {
        private readonly DiscordSocketClient _client;
        private readonly IKvKScheduleService _kvkScheduleService;
        private const ulong KvKAnnouncementChannelId = 1516289806290915429;
        public KvKAnnouncementWorker(
            DiscordSocketClient client,
            IKvKScheduleService kvkScheduleService)
        {
            _client = client;
            _kvkScheduleService = kvkScheduleService;
        }
        public async Task StartAsync(CancellationToken token)
        {
            await Task.Delay(TimeSpan.FromSeconds(10), token);
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await ProcessUpcomingEvents();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[KvKWorker Error] {ex}");
                }
                await Task.Delay(TimeSpan.FromMinutes(5), token);
            }
        }
        private async Task ProcessUpcomingEvents()
        {
            // DEBUG LOGS 
            Console.WriteLine("[KvKWorker] Tick");
            var upcoming = await _kvkScheduleService
                .GetUpcomingEventsAsync(TimeSpan.FromHours(6));
            if (upcoming.Count == 0)
                return;
            var channel = await _client.GetChannelAsync(KvKAnnouncementChannelId) as IMessageChannel;
            if (channel == null)
            {
                Console.WriteLine($"[KvKWorker] Channel NOT FOUND: {KvKAnnouncementChannelId}");
                return;
            }
            foreach (var evt in upcoming.Where(e => !e.AnnouncementSent))
            {
                Console.WriteLine(
                  $"[KvKWorker] Event {evt.EventId} | " +
                  $"Start={evt.StartUtc:o} | Kind={evt.StartUtc.Kind} | " +
                  $"Now={DateTime.UtcNow:o} | " +
                  $"Announced={evt.AnnouncementSent}");
                var timeLeft = evt.StartUtc - DateTime.UtcNow;
                if (timeLeft.TotalHours > 6 || timeLeft.TotalMinutes < 0)
                    continue;

                // Free-text event type — no longer restricted to gate/killingfield

                // Plain text only (no embed) so:
                // 1. Mentions in Description (e.g. @everyone, @here, <@&roleId>) actually ping —
                //    Discord only fires notifications for mentions in message content, not embeds.
                // 2. Kiki can parse a single plain-text message; it can't handle content + embed together.
                string messageText =
                    (string.IsNullOrWhiteSpace(evt.Description) ? "" : $"{evt.Description}\n\n") +
                    $"⏰ **{evt.EventType} Incoming**\n" +
                    $"Starts: <t:{ToUnix(evt.StartUtc)}:F> (<t:{ToUnix(evt.StartUtc)}:R>)";

                await channel.SendMessageAsync(
                    text: messageText,
                    allowedMentions: AllowedMentions.All
                );

                await _kvkScheduleService.MarkAnnouncedAsync(evt.EventId);
            }
        }
        private static long ToUnix(DateTime utc)
            => new DateTimeOffset(utc).ToUnixTimeSeconds();
    }
}