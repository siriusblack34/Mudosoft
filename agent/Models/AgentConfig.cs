namespace Mudosoft.Agent.Models;

public sealed class AgentConfig
{
    // Backend'in gördüğü benzersiz cihaz ID'si
    public string DeviceId { get; set; } = "DEV-001";

    // Backend base URL
    public string BackendUrl { get; set; } = "http://0.0.0.0:5102";
    
    // 🏆 KRİTİK EKLEME: StoreCode eklendi
    public string? StoreCode { get; set; } 

    // ✅ AgentWorker'daki Hataları Çözen eski int tipleri
    public int HeartbeatIntervalSeconds { get; set; } = 10;
    public int CommandPollIntervalSeconds { get; set; } = 10;

    // İstersen sabit IP override etmek için (opsiyonel)
    public string? IpAddress { get; set; }
}