using Microsoft.Extensions.Configuration;
using MudoSoft.Backend.Models;
using System.Threading.Tasks;

namespace MudoSoft.Backend.Services
{
    public class WmiSystemInfoService : IWmiSystemInfoService
    {
        public WmiSystemInfoService(IConfiguration config)
        {
        }

        public async Task<WmiSystemInfo?> GetSystemInfo(string ip)
        {
            // TEMPORARY FIX: WMI Service disabled to unblock build.
            // System.Management dependency issues.
            return await Task.FromResult<WmiSystemInfo?>(null);
        }
    }
}
