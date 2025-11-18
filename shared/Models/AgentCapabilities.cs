namespace Mudosoft.Shared.Models;

public sealed class AgentCapabilities
{
    public bool CanExecuteCommands { get; set; }
    public bool HasWatchdogs { get; set; }
    public bool SupportsSelfHealing { get; set; }
    public bool SupportsPeripheralChecks { get; set; }
    public string AgentVersion { get; set; } = "1.0.0";
}
