using System.Threading.Tasks;
using Orchestra.Backend.Models;

namespace Orchestra.Backend.Services
{
    public interface IWmiSystemInfoService
    {
        Task<WmiSystemInfo?> GetSystemInfo(string ip);
    }
}
