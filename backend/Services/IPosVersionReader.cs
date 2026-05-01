using System.Threading.Tasks;

namespace Orchestra.Backend.Services
{
    public interface IPosVersionReader
    {
        Task<string?> GetVersion(string ip);
    }
}
