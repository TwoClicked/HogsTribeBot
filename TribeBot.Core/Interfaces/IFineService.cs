using TribeBot.Core.Entities;

namespace TribeBot.Core.Interfaces
{
    public interface IFineService
    {
        Task AddFineAsync(FineRecord fine);
        Task<List<FineRecord>> GetAllFinesAsync();
        Task<List<FineRecord>> GetUnpaidFinesAsync();
        Task<List<FineRecord>> GetPaidFinesAsync();
        Task<List<FineRecord>> GetFinesForUserAsync(string discordUserId);

        Task AddPaymentAsync(string discordUserId, int amount);

        Task AddReignFineAsync(Member member, int amount, string notes);
        Task AddEventFineAsync(Member member, int amount, string notes);

        Task RemoveFineAsync(string fineId);

        Task ReduceReignStrikesAsync(); // called on !lockreign
        Task UpdateFineAsync(FineRecord fine);
        Task<bool> AddBankFineAsync(Member member, int amount, DateTime weekStartUtc, string reason
);


    }
}
