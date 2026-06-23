using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TribeBot.Core.Entities;

namespace TribeBot.Core.Interfaces
{
    public interface IKvKScheduleService
    {
        Task AddTimedEventAsync(
            string kvkId,
            string eventType,
            DateTime startUtc,
            string description);

        Task<List<KvKTimedEvent>> GetUpcomingEventsAsync(
            TimeSpan within);

        Task MarkAnnouncedAsync(string eventId);
        Task<List<KvKTimedEvent>> GetTimedEventsForKvKAsync(string kvkId);
    }
}
