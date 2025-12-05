using Discord.WebSocket;
using System.Threading.Tasks;

namespace TribeBot.Core.Flows.Interfaces
{
    public interface IFlow
    {
        ulong UserId { get; }

        /// <summary>
        /// Handles incoming messages for this flow.
        /// Returns true when the flow handled the message (even if not completed).
        /// </summary>
        Task<bool> HandleAsync(SocketMessage message);
    }
}
