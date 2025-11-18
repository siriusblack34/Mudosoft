namespace MudoSoft.Backend.Models;

public class ActionRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceHostname { get; set; } = string.Empty;
    public int StoreCode { get; set; }

    public string Type { get; set; } = string.Empty; // reboot, run_ps, sql_query...
    public string Status { get; set; } = "pending";  // pending | running | success | failed

    public string RequestedBy { get; set; } = "system";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }

    public string? Summary { get; set; }
}
