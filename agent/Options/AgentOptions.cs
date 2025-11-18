namespace Mudosoft.Agent.Options;

public sealed class AgentOptions
{
    public string ServerUrl { get; set; } = "http://localhost:5102";
    public string DeviceId { get; set; } = "";        // auto-146-192.168.146.31 gibi
    public string StoreCode { get; set; } = "";
    public int HeartbeatIntervalSeconds { get; set; } = 30;
    public int CommandPollIntervalSeconds { get; set; } = 10;
}
