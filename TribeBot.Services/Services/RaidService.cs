using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TribeBot.Core.DTOS;
using TribeBot.Core.Entities;
using TribeBot.Core.Enums;
using TribeBot.Core.Interfaces;
using TribeBot.Data.Interfaces;

namespace TribeBot.Services.Services
{
    public class RaidService : IRaidService
    {
        private readonly IGoogleSheetsDataStore _store;

        public RaidService(IGoogleSheetsDataStore store)
        {
            _store = store;
        }

        // ======================================================
        // CREATE RAID
        // ======================================================
        public async Task<Raid> CreateRaidAsync(
            RaidType raidType,
            DateTime startUtc,
            ulong channelId,
            ulong messageId)
        {
            if (startUtc.Kind != DateTimeKind.Utc)
                throw new ArgumentException("Start time must be UTC");

            var raid = new Raid
            {
                RaidId = Guid.NewGuid().ToString("N"),
                RaidType = raidType,
                StartUtc = startUtc,
                ChannelId = channelId,
                MessageId = messageId,
                IsClosed = false
            };

            await _store.CreateRaidAsync(raid);
            return raid;
        }

        // ======================================================
        // LOOKUP
        // ======================================================
        public Task<Raid?> GetRaidByMessageAsync(ulong messageId)
            => _store.GetRaidByMessageIdAsync(messageId);

        // ======================================================
        // SIGNUP
        // ======================================================
        public async Task RegisterSignupAsync(
            string raidId,
            ulong userId,
            RaidSignupResponse response)
        {
            var signup = new RaidSignup
            {
                RaidId = raidId,
                UserId = userId,
                Response = response,
                UpdatedUtc = DateTime.UtcNow
            };

            await _store.UpsertRaidSignupAsync(signup);
        }

        // ======================================================
        // SUMMARY
        // ======================================================
        public async Task<RaidSignupSummary> GetSignupSummaryAsync(string raidId)
        {
            var signups = await _store.GetRaidSignupsAsync(raidId);

            return new RaidSignupSummary
            {
                Yes = signups
                    .Where(s => s.Response == RaidSignupResponse.Yes)
                    .Select(s => s.UserId)
                    .ToList(),

                No = signups
                    .Where(s => s.Response == RaidSignupResponse.No)
                    .Select(s => s.UserId)
                    .ToList(),

                Maybe = signups
                    .Where(s => s.Response == RaidSignupResponse.Maybe)
                    .Select(s => s.UserId)
                    .ToList()
            };
        }
    }

}
