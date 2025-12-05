using System.Collections.Generic;
using TribeBot.Core.Flows.Interfaces;

namespace TribeBot.Core.Flows
{
    public class UserFlowManager : IUserFlowManager
    {
        private readonly Dictionary<ulong, IFlow> _activeFlows = new();

        public void StartFlow(ulong userId, IFlow flow)
        {
            _activeFlows[userId] = flow;
        }

        public bool IsInFlow(ulong userId)
            => _activeFlows.ContainsKey(userId);

        public IFlow? GetFlow(ulong userId)
            => _activeFlows.TryGetValue(userId, out var flow) ? flow : null;

        public void EndFlow(ulong userId)
        {
            if (_activeFlows.ContainsKey(userId))
                _activeFlows.Remove(userId);
        }
    }
}
