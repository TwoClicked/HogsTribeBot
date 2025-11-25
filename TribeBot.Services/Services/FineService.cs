using TribeBot.Core.Entities;
using TribeBot.Data.Interfaces;
using TribeBot.Services.Interfaces;

namespace TribeBot.Services.Services
{
    public class FineService : IFineService
    {
        private readonly IGoogleSheetsDataStore _dataStore;
        private readonly IMemberService _memberService;

        public FineService(IGoogleSheetsDataStore dataStore, IMemberService memberService)
        {
            _dataStore = dataStore;
            _memberService = memberService;
        }

        public Task<List<FineRecord>> GetAllFinesAsync()
        {
            return _dataStore.GetAllFinesAsync();
        }

        public async Task<List<FineRecord>> GetUnpaidFinesAsync()
        {
            var fines = await _dataStore.GetAllFinesAsync();
            return fines.Where(f => !f.IsPaid).ToList();
        }

        public async Task<List<FineRecord>> GetPaidFinesAsync()
        {
            var fines = await _dataStore.GetAllFinesAsync();
            return fines.Where(f => f.IsPaid).ToList();
        }

        public async Task<List<FineRecord>> GetFinesForUserAsync(string discordUserId)
        {
            var fines = await _dataStore.GetAllFinesAsync();
            return fines.Where(f => f.DiscordUserId == discordUserId).ToList();
        }

        public async Task AddEventFineAsync(Member member, int amount, string notes)
        {
            var fine = new FineRecord
            {
                FineId = Guid.NewGuid().ToString(),
                DiscordUserId = member.DiscordUserId,
                IngameName = member.IngameName,
                Amount = amount,
                FineType = "Event",
                IsPaid = false,
                PaidAmount = 0,
                ReignStrikes = 0,
                Notes = notes,
                IssuedAtUtc = DateTime.UtcNow
            };

            await _dataStore.AddFineAsync(fine);
        }

        public async Task AddReignFineAsync(Member member, int amount, string notes)
        {
            // Load ALL previous Reign fines (paid or unpaid)
            var allFines = await _dataStore.GetAllFinesAsync();

            int previousReignFines = allFines.Count(f =>
                f.DiscordUserId == member.DiscordUserId &&
                f.FineType == "Reign"
            );

            // First offense = 0 strikes
            // Second or more = 2 strikes
            int strikesToAdd = previousReignFines >= 1 ? 2 : 0;

            var fine = new FineRecord
            {
                FineId = Guid.NewGuid().ToString(),
                DiscordUserId = member.DiscordUserId,
                IngameName = member.IngameName,
                Amount = amount,
                FineType = "Reign",
                IsPaid = false,
                PaidAmount = 0,
                ReignStrikes = strikesToAdd,
                Notes = notes,
                IssuedAtUtc = DateTime.UtcNow
            };

            await _dataStore.AddFineAsync(fine);
        }


        public async Task AddFineAsync(FineRecord fine)
        {
            await _dataStore.AddFineAsync(fine);
        }

        public async Task AddPaymentAsync(string discordUserId, int amount)
        {
            var fines = await _dataStore.GetAllFinesAsync();
            var userFines = fines
                .Where(f => f.DiscordUserId == discordUserId && !f.IsPaid)
                .OrderBy(f => f.IssuedAtUtc)
                .ToList();

            int remaining = amount;

            foreach (var fine in userFines)
            {
                if (remaining <= 0)
                    break;

                int owed = fine.Amount - fine.PaidAmount;

                if (remaining >= owed)
                {
                    // fully pay fine
                    fine.PaidAmount += owed;
                    fine.IsPaid = true;
                    remaining -= owed;
                }
                else
                {
                    // partially pay
                    fine.PaidAmount += remaining;
                    remaining = 0;
                }

                await _dataStore.UpdateFineAsync(fine);
            }
        }

        public async Task RemoveFineAsync(string fineId)
        {
            await _dataStore.RemoveFineByIdAsync(fineId);
        }

        public async Task ReduceReignStrikesAsync()
        {
            var fines = await _dataStore.GetAllFinesAsync();

            foreach (var fine in fines.Where(f => f.FineType == "Reign" && f.ReignStrikes > 0))
            {
                fine.ReignStrikes--;

                await _dataStore.UpdateFineAsync(fine);
            }
        }
    }
}
