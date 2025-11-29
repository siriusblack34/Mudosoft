// siriusblack34/mudosoft/Mudosoft-c953f6e12102eb9684317565a375036f8ff09c4f/shared/Dtos/DeviceDetailsDto.cs

namespace Mudosoft.Shared.Dtos
{
    // ❌ KALDIRILDI: Bu dosyadaki OsInfoDto tanımı kaldırıldı.
    // Çünkü OsInfoDto, kendi dosyasında (OsInfoDto.cs) zaten tanımlıdır.

    // Cihazın genel detaylarını taşıyan ana DTO
    public class DeviceDetailsDto
    {
        public string Id { get; set; } = string.Empty;
        public string Hostname { get; set; } = string.Empty;
        public string Ip { get; set; } = string.Empty;
        public string Store { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty; // Controller'da string'e dönüştürülmeli
        public bool Online { get; set; }
        public DateTime? LastSeen { get; set; }
        
        // OsInfoDto tipindeki nesne (Kendi dosyasından referans veriliyor)
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
        
        // 💡 NOT: Frontend'in Metrics listesini çekebilmesi için bu DTO'ya bir Metrics alanı eklenmemiştir.
        // Bunun yerine, DevicesController.cs içindeki GetById metodu yerel (local) DeviceDetailDto'yu kullanır.
        // Bu yapı Shared kütüphanesini temiz tutar.
    }
}