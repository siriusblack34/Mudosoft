namespace MudoSoft.Backend.Dtos;

public class DashboardResponseDto
{
    public int TotalDevices { get; set; }
    public int Online { get; set; }
    public int Offline { get; set; }
    public int Healthy { get; set; }
    public int Warning { get; set; }
    public int Critical { get; set; }
    public List<RecentOfflineDeviceDto> RecentOffline { get; set; } = new();
}

public class RecentOfflineDeviceDto
{
    public string Hostname { get; set; } = default!;
    public string Ip { get; set; } = default!;
    public string Os { get; set; } = default!;
    public int Store { get; set; }
    public string LastSeen { get; set; } = default!;
}