namespace Mudosoft.Shared.Dtos
{
    // Cihazın işletim sistemi bilgilerini taşıyan DTO
    // Bu sınıf, DeviceDetailsDto'nun içinde kullanıldığı için burada tanımlanmalıdır.

    // Cihazın genel detaylarını taşıyan ana DTO
    public class DeviceDetailsDto
    {
        public string Id { get; set; } = string.Empty;
        public string Hostname { get; set; } = string.Empty;
        public string Ip { get; set; } = string.Empty;
        
        // Controller'daki ToString() kullanımı için bu alanların string olması gerekiyor.
        public string Store { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        
        public bool Online { get; set; }
        public DateTime? LastSeen { get; set; }
        
        // OsInfoDto, aynı namespace/dosya içinde tanımlıdır.
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