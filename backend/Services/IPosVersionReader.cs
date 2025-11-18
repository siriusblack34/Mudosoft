using System.Threading.Tasks;

namespace MudoSoft.Backend.Services
{
    public interface IPosVersionReader
    {
        Task<string?> GetVersion(string ip);
    }
}
