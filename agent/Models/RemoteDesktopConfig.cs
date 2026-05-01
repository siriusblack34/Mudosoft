namespace Orchestra.Agent.Models;

public class RemoteDesktopConfig
{
    public string Mode { get; set; } = "Manager"; // "Manager" (Service) vs "Helper" (Console)
}
