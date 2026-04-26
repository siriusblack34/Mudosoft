namespace MudoSoft.Backend.Models
{
    public class StoreDeviceWithStatusDto
    {
        public string DeviceId { get; set; } = "";
        public int StoreCode { get; set; }
        public string StoreName { get; set; } = "";
        public string DeviceType { get; set; } = "";
        public string DeviceName { get; set; } = "";
        public string CalculatedIpAddress { get; set; } = "";
        public bool IsOnline { get; set; }
        /// <summary>ICMP Ping sonucu (PC/Kasa icin). null = kontrol yapilmadi</summary>
        public bool? PingReachable { get; set; }
        /// <summary>TCP 1433 SQL Server sonucu (PC/Kasa icin). null = kontrol yapilmadi</summary>
        public bool? SqlReachable { get; set; }
        public DateTime? LastSeen { get; set; }
        public bool IsTemporarilyClosed { get; set; }
        public string? TemporaryCloseReason { get; set; }

        /// <summary>Router icin son olculen ping RTT (ms). Router degilse null.</summary>
        public int? LatencyMs { get; set; }
    }
}
