namespace MudoSoft.Backend.Models
{
    public class DashboardDto
    {
        public int TotalDevices { get; set; }
        public int Online { get; set; }
        public int Offline { get; set; }

        public int Healthy { get; set; }
        public int Warning { get; set; }
        public int Critical { get; set; }

        public List<RecentOfflineDevice> RecentOffline { get; set; } = new();
    }

    public class RecentOfflineDevice
    {
        public string Hostname { get; set; } = "";
        public string Ip { get; set; } = "";
        public string? Os { get; set; }
        public int Store { get; set; }
        public string LastSeen { get; set; } = "";
    }
}
