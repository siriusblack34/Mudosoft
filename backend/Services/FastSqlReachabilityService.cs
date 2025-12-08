using System.Net.Sockets;
using System.Threading.Tasks;

namespace MudoSoft.Backend.Services
{
    public class FastSqlReachabilityService
    {
        public async Task<bool> IsSqlReachable(string ip, int port)
        {
            try
            {
                using var client = new TcpClient();

                var connectTask = client.ConnectAsync(ip, port);
                var timeoutTask = Task.Delay(500); // 0.5 sn timeout

                var finished = await Task.WhenAny(connectTask, timeoutTask);
                return finished == connectTask && client.Connected;
            }
            catch
            {
                return false;
            }
        }
    }
}
