// agent/Models/AgentConfig.cs
namespace Mudosoft.Agent.Models;

public sealed class AgentConfig
{
    // Backend'in gördüğü benzersiz cihaz ID'si
    public string DeviceId { get; set; } = "DEV-001";

    // Backend base URL
    public string BackendUrl { get; set; } = "http://localhost:5102";

    // Kaç saniyede bir heartbeat atsın
    public int HeartbeatIntervalSeconds { get; set; } = 10;

    // Kaç saniyede bir komut poll etsin
    public int CommandPollIntervalSeconds { get; set; } = 10;

    // İstersen sabit IP override etmek için (opsiyonel)
    public string? IpAddress { get; set; }
}
