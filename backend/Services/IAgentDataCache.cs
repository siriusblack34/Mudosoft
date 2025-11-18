using MudoSoft.Backend.Models;

namespace MudoSoft.Backend.Services
{
    public interface IAgentDataCache
    {
        bool TryGet(string ip, out AgentReport? data);
        void Update(string ip, AgentReport report);
        IReadOnlyDictionary<string, AgentReport> GetAll();
    }
}
