using System.Collections.Generic;

namespace Orchestra.Shared.Dtos
{
    public class DashboardDto
    {
        public int TotalDevices { get; set; }
        public int Online { get; set; }
        public int Offline { get; set; }

        public List<RecentOfflineDevice>? RecentOffline { get; set; }
    }
}
