using System;
using System.Threading.Tasks;
using TribeBot.Core.DTOS;
using TribeBot.Core.Entities;
using TribeBot.Core.Enums;

namespace TribeBot.Core.Interfaces
{
    public interface IRaidService
    {
        Task<Raid> CreateRaidAsync(
            string raidType,
            DateTime startUtc,
            ulong channelId,
            ulong messageId);

        Task<Raid?> GetRaidByMessageAsync(ulong messageId);

        Task RegisterSignupAsync(
            string raidId,
            ulong userId,
            RaidSignupResponse response);

        Task<RaidSignupSummary> GetSignupSummaryAsync(string raidId);
    }
}
