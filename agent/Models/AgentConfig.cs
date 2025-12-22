namespace Mudosoft.Agent.Models;

public sealed class AgentConfig
{
    // Backend'in gördüğü benzersiz cihaz ID'si
    public string DeviceId { get; set; } = "DEV-001";

    // Backend base URL
    public string BackendUrl { get; set; } = "http://0.0.0.0:5102";
    
    // 🏆 KRİTİK EKLEME: StoreCode eklendi
    public string? StoreCode { get; set; } 

    // ✅ OPTIMIZED: Changed to double for sub-second intervals
    public double HeartbeatIntervalSeconds { get; set; } = 5;
    public double CommandPollIntervalSeconds { get; set; } = 0.5; // 500ms for fast File Manager

    // İstersen sabit IP override etmek için (opsiyonel)
    public string? IpAddress { get; set; }
}