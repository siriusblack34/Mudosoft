using Microsoft.Extensions.Options;
using Mudosoft.Agent.Models;

namespace Mudosoft.Agent.Services;

public sealed class DeviceIdentityProvider
{
    private readonly AgentConfig _config;

    public string DeviceId => _config.DeviceId;

    public DeviceIdentityProvider(IOptions<AgentConfig> cfg)
    {
        _config = cfg.Value;
    }
}
