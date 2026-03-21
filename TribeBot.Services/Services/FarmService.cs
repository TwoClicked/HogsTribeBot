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

        var assignment = await _dataStore.GetAssignmentForUserAsync(ownerDiscordId);
        if (assignment != null)
        {
            var tribe = await _dataStore.GetFarmTribeByIdAsync(assignment.FarmTribeId);
            if (tribe != null)
            {
                if (tribe.UsedSlots + 1 > tribe.TotalSlots)
                    throw new InvalidOperationException(
                        "Your farm tribe is at capacity. Contact an officer.");

                tribe.UsedSlots += 1;
                await _dataStore.UpdateFarmTribeAsync(tribe);
            }
        }

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

        await _dataStore.RemoveFarmAsync(farmId);

        var assignment = await _dataStore.GetAssignmentForUserAsync(discordUserId);
        if (assignment == null)
            return;

        var tribe = await _dataStore.GetFarmTribeByIdAsync(assignment.FarmTribeId);
        if (tribe == null)
            return;

        tribe.UsedSlots = Math.Max(0, tribe.UsedSlots - 1);
        await _dataStore.UpdateFarmTribeAsync(tribe);
    }

    public async Task UpdateFarmAsync(
        string oldFarmId,
        string newFarmId,
        string newFarmName,
        string userId)
    {
        var farm = await _dataStore.GetFarmByIdAsync(oldFarmId);

        if (farm == null)
            throw new InvalidOperationException("Farm not found.");

        if (farm.OwnerDiscordId != userId)
            throw new InvalidOperationException("You do not own this farm.");

        if (string.IsNullOrWhiteSpace(newFarmName))
            throw new InvalidOperationException("Farm name cannot be empty.");

        if (!newFarmId.All(char.IsDigit))
            throw new InvalidOperationException("Farm ID must contain only numbers.");

        if (oldFarmId != newFarmId)
        {
            var existing = await _dataStore.GetFarmByIdAsync(newFarmId);
            if (existing != null)
                throw new InvalidOperationException("This farm ID is already registered.");
        }

        oldFarmId = oldFarmId.Trim();
        newFarmId = newFarmId.Trim();

        farm.FarmId = newFarmId;
        farm.FarmName = newFarmName;

        await _dataStore.UpdateFarmAsync(oldFarmId, farm); 
    }

    public Task<List<Farm>> GetAllFarmsAsync()
        => _dataStore.GetAllFarmsAsync();
}