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
    }
}
