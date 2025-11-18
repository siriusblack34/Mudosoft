namespace MudoSoft.Backend.Models;

public class Alert
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string DeviceId { get; set; } = "";
    public string IpAddress { get; set; } = "";
    public string Hostname { get; set; } = "";
    public string Level { get; set; } = ""; // Warning, Critical
    public string Message { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
