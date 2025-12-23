using TribeBot.Core.Entities;

namespace TribeBot.Core.Interfaces
{
    public interface IFarmTribeService
    {
        Task CreateFarmTribeAsync(string name, int totalSlots);
        Task<List<FarmTribe>> GetAllFarmTribesAsync();
        Task<FarmTribe?> GetFarmTribeByIdAsync(string farmTribeId);
        Task<FarmTribe?> GetFarmTribeByNameAsync(string farmTribeName);
        Task UpdateFarmTribeAsync(FarmTribe tribe);
        Task EditFarmTribeAsync(string farmTribeId, string? newName, int? newTotalSlots);
        Task DeleteFarmTribeAsync(string farmTribeId);

    }
}
