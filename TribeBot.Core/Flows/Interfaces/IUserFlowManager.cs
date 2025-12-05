using TribeBot.Core.Flows.Interfaces;

namespace TribeBot.Core.Flows.Interfaces
{
    public interface IUserFlowManager
    {
        void StartFlow(ulong userId, IFlow flow);
        bool IsInFlow(ulong userId);
        IFlow? GetFlow(ulong userId);
        void EndFlow(ulong userId);
    }
}
