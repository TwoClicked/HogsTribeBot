using TribeBot.Core.Entities;
using TribeBot.Core.Interfaces;
using TribeBot.Data.Interfaces;

public class FarmService : IFarmService
{
    private readonly IGoogleSheetsDataStore _dataStore;

    public FarmService(IGoogleSheetsDataStore dataStore)
    {
        _dataStore = dataStore;
    }

    public async Task RegisterFarmAsync(
        string farmId,
        string farmName,
        string ownerDiscordId,
        string ownerIngameName)
    {
        if (string.IsNullOrWhiteSpace(farmName))
            throw new InvalidOperationException("Farm name cannot be empty");

        if (!farmId.All(char.IsDigit))
            throw new InvalidOperationException("Farm ID must contain only numbers");

        var existing = await _dataStore.GetFarmByIdAsync(farmId);
        if (existing != null)
            throw new InvalidOperationException("This farm ID is already registered.");

        // Only enforce slots if player is assigned to a farm tribe
        var assignment = await _dataStore.GetAssignmentForUserAsync(ownerDiscordId);
        if (assignment != null)
        {
            var tribe = await _dataStore.GetFarmTribeByIdAsync(assignment.FarmTribeId);
            if (tribe != null)
            {
                if (tribe.UsedSlots + 1 > tribe.TotalSlots)
                    throw new InvalidOperationException(
                        "Your farm tribe is at capacity. Contact an officer to place this farm.");

                tribe.UsedSlots += 1;
                await _dataStore.UpdateFarmTribeAsync(tribe);
            }
        }

        // Finally, register the farm
        await _dataStore.AddFarmAsync(new Farm
        {
            FarmId = farmId,
            FarmName = farmName,
            OwnerDiscordId = ownerDiscordId,
            OwnerIngameName = ownerIngameName,
            RegisteredUtc = DateTime.UtcNow
        });
    }


    public Task<List<Farm>> GetFarmsForUserAsync(string discordUserId)
        => _dataStore.GetFarmsByOwnerAsync(discordUserId);

    public Task<Farm?> GetFarmByIdAsync(string farmId)
        => _dataStore.GetFarmByIdAsync(farmId);

    public async Task RemoveFarmAsync(string farmId, string discordUserId)
    {
        var farm = await _dataStore.GetFarmByIdAsync(farmId);
        if (farm == null)
            throw new InvalidOperationException("Farm not found.");

        if (farm.OwnerDiscordId != discordUserId)
            throw new InvalidOperationException("You do not own this farm.");

        // Remove the farm first
        await _dataStore.RemoveFarmAsync(farmId);

        // Check if player is assigned to a farm tribe
        var assignment = await _dataStore.GetAssignmentForUserAsync(discordUserId);
        if (assignment == null)
            return;

        var tribe = await _dataStore.GetFarmTribeByIdAsync(assignment.FarmTribeId);
        if (tribe == null)
            return;

        // Decrement used slots safely
        tribe.UsedSlots = Math.Max(0, tribe.UsedSlots - 1);
        await _dataStore.UpdateFarmTribeAsync(tribe);
    }

    public Task<List<Farm>> GetAllFarmsAsync()
        => _dataStore.GetAllFarmsAsync();
}
