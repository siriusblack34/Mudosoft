using System.Threading.Tasks;
using MudoSoft.Backend.Models;

namespace MudoSoft.Backend.Services
{
    public interface IWmiSystemInfoService
    {
        Task<WmiSystemInfo?> GetSystemInfo(string ip);
    }
}
