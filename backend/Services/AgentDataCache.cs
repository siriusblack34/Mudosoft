using MudoSoft.Backend.Models;
using System.Collections.Concurrent;

namespace MudoSoft.Backend.Services
{
    public class AgentDataCache : IAgentDataCache
    {
        private readonly ConcurrentDictionary<string, AgentReport> _cache = new();

        public bool TryGet(string ip, out AgentReport? data)
        {
            return _cache.TryGetValue(ip, out data);
        }

        public void Update(string ip, AgentReport report)
        {
            _cache[ip] = report;
        }

        public IReadOnlyDictionary<string, AgentReport> GetAll() => _cache;
    }
}
