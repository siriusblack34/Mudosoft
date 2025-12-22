using Mudosoft.Agent.Interfaces;

namespace Mudosoft.Agent.Services;

/// <summary>
/// Wrapper for IDeviceIdentityProvider that can be injected as concrete type
/// </summary>
public class IdentityProvider
{
    private readonly IDeviceIdentityProvider _inner;

    public IdentityProvider(IDeviceIdentityProvider inner)
    {
        _inner = inner;
    }

    public string GetDeviceId() => _inner.GetDeviceId();
}
