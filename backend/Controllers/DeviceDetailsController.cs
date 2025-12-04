namespace Mudosoft.Shared.Dtos
{

    public class DeviceDetailsDto
    {
        public string Id { get; set; } = string.Empty;
        public string Hostname { get; set; } = string.Empty;
        public string Ip { get; set; } = string.Empty;
        public string Store { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty; // Controller'da string'e dönüştürülmeli
        public bool Online { get; set; }
        public DateTime? LastSeen { get; set; }
        
        // OsInfoDto tipindeki nesne
        public OsInfoDto Os { get; set; } = new OsInfoDto(); 
        
        public string AgentVersion { get; set; } = string.Empty;
        public bool Agent { get; set; } = false;

        // Performans metrikleri
        public int? Cpu { get; set; }
        public int? Ram { get; set; }
        public int? Disk { get; set; }
        
        // Versiyon bilgileri
        public string? SqlVersion { get; set; }
        public string? PosVersion { get; set; }
    }
}