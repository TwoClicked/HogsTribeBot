using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TribeBot.Core.Entities;
using TribeBot.Core.Interfaces;
using TribeBot.Data.Interfaces;

namespace TribeBot.Services.Services
{
    public class KvKScheduleService : IKvKScheduleService
    {
        private readonly IGoogleSheetsDataStore _dataStore;

        public KvKScheduleService(IGoogleSheetsDataStore dataStore)
        {
            _dataStore = dataStore;
        }

        public async Task AddTimedEventAsync(
            string kvkId,
            string eventType,
            DateTime startUtc)
        {
            var evt = new KvKTimedEvent
            {
                EventId = Guid.NewGuid().ToString("N"),
                KvKId = kvkId,
                EventType = eventType,
                StartUtc = startUtc,
                AnnouncementSent = false
            };

            await _dataStore.AddKvKTimedEventAsync(evt);
        }

        public async Task<List<KvKTimedEvent>> GetUpcomingEventsAsync(TimeSpan within)
        {
            var now = DateTime.UtcNow;
            var all = await _dataStore.GetAllKvKTimedEventsAsync();

            return all.Where(e =>
                !e.AnnouncementSent &&
                e.StartUtc > now &&
                e.StartUtc <= now.Add(within)
            ).ToList();
        }

        public Task MarkAnnouncedAsync(string eventId)
        {
            return UpdateAnnouncementFlag(eventId);
        }

        private async Task UpdateAnnouncementFlag(string eventId)
        {
            var all = await _dataStore.GetAllKvKTimedEventsAsync();
            var evt = all.FirstOrDefault(x => x.EventId == eventId);
            if (evt == null) return;

            evt.AnnouncementSent = true;
            await _dataStore.UpdateKvKTimedEventAsync(evt);
        }
    }

}
