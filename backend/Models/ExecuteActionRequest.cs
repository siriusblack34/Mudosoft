namespace MudoSoft.Backend.Models;

public class ExecuteActionRequest
{
    public string DeviceId { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public Dictionary<string, object>? Parameters { get; set; }
}
