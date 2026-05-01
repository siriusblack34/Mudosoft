namespace Orchestra.Agent.Interfaces;

public interface ISystemInfoService : IDisposable 
{
    // Performance Metrics
    double GetCpuUsage();
    double GetRamUsage();
    double GetDiskUsage();
    double? GetDiskDUsage();
    long? GetTotalDiskDGB();
    
    // System Info
    string GetOsName();
    
    // Hardware Inventory
    string GetCpuModel();
    long GetTotalRamMB();
    long GetTotalDiskGB();
    string? GetGpuModel();
    
    // User & Session
    string? GetLastLoggedInUser();
    
    // Uptime
    DateTime GetSystemBootTime();
}
