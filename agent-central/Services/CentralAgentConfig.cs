namespace Orchestra.CentralAgent.Services;

public class CentralAgentConfig
{
    public string BackendUrl    { get; set; } = "http://10.75.1.109";
    public string ServiceName   { get; set; } = "OrchestraCentralAgent";
    public string DeviceIdFile  { get; set; } = @"C:\ProgramData\OrchestraCentralAgent\device_id.dat";
}
