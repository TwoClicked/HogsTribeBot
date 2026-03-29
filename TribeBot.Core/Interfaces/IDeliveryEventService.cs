using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TribeBot.Core.Entities;

namespace TribeBot.Core.Interfaces
{
    public interface IDeliveryEventService
    {
        Task<string> StartEventAsync();

        string? GetActiveEventId();
        Task<string?> GetActiveEventIdAsync();

        Task RegisterBraceletAsync(Member member, int amount);
        Task RegisterGoldAsync(Member member, int amount);

        Task<List<string>> GetNonParticipantsAsync(string eventId);

        Task<bool> HasUserParticipatedAsync(string eventId, string discordUserId);


        Task<List<Member>> EndEventAsync(string eventId);
    }
}