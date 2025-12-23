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
    public class FarmTribeAssignmentService : IFarmTribeAssignmentService
    {
        private readonly IGoogleSheetsDataStore _dataStore;

        public FarmTribeAssignmentService(IGoogleSheetsDataStore dataStore)
        {
            _dataStore = dataStore;
        }

        public async Task AssignPlayerAsync(string discordUserId, string farmTribeId)
        {
            var tribe = await _dataStore.GetFarmTribeByIdAsync(farmTribeId)
                ?? throw new InvalidOperationException("Farm tribe not found.");

            var farms = await _dataStore.GetFarmsByOwnerAsync(discordUserId);
            int farmCount = farms.Count;

            if (farmCount == 0)
                throw new InvalidOperationException("Player has no registered farms.");

            if (tribe.UsedSlots + farmCount > tribe.TotalSlots)
                throw new InvalidOperationException(
                    $"Not enough slots. Tribe has {tribe.TotalSlots - tribe.UsedSlots} available.");

            var existing = await _dataStore.GetAssignmentForUserAsync(discordUserId);
            if (existing != null)
                throw new InvalidOperationException("Player is already assigned to a farm tribe.");

            // Assign
            await _dataStore.AddAssignmentAsync(new PlayerFarmTribeAssignment
            {
                DiscordUserId = discordUserId,
                FarmTribeId = farmTribeId,
                AssignedUtc = DateTime.UtcNow
            });

            // Update used slots
            tribe.UsedSlots += farmCount;
            await _dataStore.UpdateFarmTribeAsync(tribe);
        }

        public async Task RemovePlayerAsync(string discordUserId)
        {
            var assignment = await _dataStore.GetAssignmentForUserAsync(discordUserId);
            if (assignment == null)
                return;

            var tribe = await _dataStore.GetFarmTribeByIdAsync(assignment.FarmTribeId);
            if (tribe == null)
                return;

            var farms = await _dataStore.GetFarmsByOwnerAsync(discordUserId);

            tribe.UsedSlots -= farms.Count;
            if (tribe.UsedSlots < 0)
                tribe.UsedSlots = 0;

            await _dataStore.UpdateFarmTribeAsync(tribe);
            await _dataStore.RemoveAssignmentAsync(discordUserId);
        }

        public async Task<PlayerFarmTribeAssignment?> GetAssignmentForUserAsync(string discordUserId)
        {
            return await _dataStore.GetAssignmentForUserAsync(discordUserId);
        }

        public async Task<List<PlayerFarmTribeAssignment>> GetAssignmentsForTribeAsync(string farmTribeId)
        {
            return await _dataStore.GetAssignmentsForTribeAsync(farmTribeId);
        }

    }

}
