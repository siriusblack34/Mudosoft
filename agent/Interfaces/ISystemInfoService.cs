using System; // IDisposable için
// ... diğer using'ler

namespace Mudosoft.Agent.Interfaces;

// IDisposable eklendi
public interface ISystemInfoService : IDisposable 
{
    double GetCpuUsage();
    double GetRamUsage();
    double GetDiskUsage();
}