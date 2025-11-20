// agent/Interfaces/ISystemInfoService.cs

namespace Mudosoft.Agent.Interfaces // Düzeltildi: Services yerine Interfaces
{
    public interface ISystemInfoService
    {
        double GetCpuUsage();
        double GetRamUsage();
        double GetDiskUsage();
    }
}