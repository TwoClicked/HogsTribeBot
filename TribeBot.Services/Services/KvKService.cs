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
    public class KvKService : IKvKService
    {
        private readonly IGoogleSheetsDataStore _dataStore;

        public KvKService(IGoogleSheetsDataStore dataStore)
        {
            _dataStore = dataStore;
        }

        public async Task<KvKEvent> CreateKvKAsync(
            string name,
            DateTime startUtc,
            DateTime endUtc)
        {
            if (endUtc <= startUtc)
                throw new InvalidOperationException("End date must be after start date.");

            var existing = await _dataStore.GetActiveKvKAsync();
            if (existing != null)
                throw new InvalidOperationException("An active KvK already exists.");

            var kvk = new KvKEvent
            {
                KvKId = Guid.NewGuid().ToString("N"),
                Name = name,
                StartUtc = startUtc,
                EndUtc = endUtc,
                IsActive = true
            };

            await _dataStore.AddKvKEventAsync(kvk);
            return kvk;
        }

        public Task<KvKEvent?> GetActiveKvKAsync()
            => _dataStore.GetActiveKvKAsync();

        public async Task EndActiveKvKAsync()
        {
            var kvk = await _dataStore.GetActiveKvKAsync();
            if (kvk == null) return;

            kvk.IsActive = false;
            await _dataStore.UpdateKvKEventAsync(kvk);
        }
    }
}
