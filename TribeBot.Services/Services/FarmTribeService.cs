using TribeBot.Core.Entities;
using TribeBot.Core.Interfaces;
using TribeBot.Data.Interfaces;

namespace TribeBot.Services.Services
{
    public class FarmTribeService : IFarmTribeService
    {
        private readonly IGoogleSheetsDataStore _dataStore;

        public FarmTribeService(IGoogleSheetsDataStore dataStore)
        {
            _dataStore = dataStore;
        }

        public async Task CreateFarmTribeAsync(string name, int totalSlots)
        {
            var existing = await GetFarmTribeByNameAsync(name);
            if (existing != null)
                throw new InvalidOperationException("A farm tribe with this name already exists.");

            var tribe = new FarmTribe
            {
                FarmTribeId = Guid.NewGuid().ToString("N")[..8],
                FarmTribeName = name.Trim(),
                TotalSlots = totalSlots,
                UsedSlots = 0,
                CreatedUtc = DateTime.UtcNow
            };

            await _dataStore.AddFarmTribeAsync(tribe);
        }

        public Task<List<FarmTribe>> GetAllFarmTribesAsync()
            => _dataStore.GetAllFarmTribesAsync();

        public Task<FarmTribe?> GetFarmTribeByIdAsync(string farmTribeId)
            => _dataStore.GetFarmTribeByIdAsync(farmTribeId);

        public async Task<FarmTribe?> GetFarmTribeByNameAsync(string farmTribeName)
        {
            var all = await _dataStore.GetAllFarmTribesAsync();
            return all.FirstOrDefault(t =>
                t.FarmTribeName.Equals(farmTribeName, StringComparison.OrdinalIgnoreCase));
        }

        public Task UpdateFarmTribeAsync(FarmTribe tribe)
            => _dataStore.UpdateFarmTribeAsync(tribe);

        public async Task EditFarmTribeAsync(string farmTribeId, string? newName, int? newTotalSlots)
        {
            var tribe = await _dataStore.GetFarmTribeByIdAsync(farmTribeId);
            if (tribe == null)
                throw new InvalidOperationException("Farm tribe not found.");

            bool changed = false;

            if (!string.IsNullOrWhiteSpace(newName) &&
                !tribe.FarmTribeName.Equals(newName, StringComparison.OrdinalIgnoreCase))
            {
                // Ensure name uniqueness
                var existing = await GetFarmTribeByNameAsync(newName);
                if (existing != null && existing.FarmTribeId != tribe.FarmTribeId)
                    throw new InvalidOperationException("Another farm tribe already uses this name.");

                tribe.FarmTribeName = newName.Trim();
                changed = true;
            }

            if (newTotalSlots.HasValue)
            {
                if (newTotalSlots.Value < tribe.UsedSlots)
                    throw new InvalidOperationException(
                        $"Total slots cannot be less than used slots ({tribe.UsedSlots}).");

                if (newTotalSlots.Value != tribe.TotalSlots)
                {
                    tribe.TotalSlots = newTotalSlots.Value;
                    changed = true;
                }
            }

            if (!changed)
                throw new InvalidOperationException("No changes were provided.");

            await _dataStore.UpdateFarmTribeAsync(tribe);
        }

        public async Task DeleteFarmTribeAsync(string farmTribeId)
        {
            var tribes = await _dataStore.GetAllFarmTribesAsync();

            var tribe = tribes.FirstOrDefault(t => t.FarmTribeId == farmTribeId);
            if (tribe == null)
                throw new InvalidOperationException("Farm tribe not found.");

            if (tribe.UsedSlots > 0)
                throw new InvalidOperationException(
                    $"Cannot delete farm tribe '{tribe.FarmTribeName}' because it still has farms assigned.");

            await _dataStore.DeleteFarmTribeAsync(farmTribeId);
        }

    }
}
