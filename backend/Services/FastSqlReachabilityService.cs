using System.Net.Sockets;

namespace MudoSoft.Backend.Services
{
    public class FastSqlReachabilityService
    {
        public async Task<bool> IsSqlReachableAsync(string ip, int port = 1433, int timeoutMs = 500)
        {
            if (string.IsNullOrWhiteSpace(ip))
                return false;

            try
            {
                using var client = new TcpClient();
                var connectTask = client.ConnectAsync(ip, port);

                var completed = await Task.WhenAny(connectTask, Task.Delay(timeoutMs));
                return completed == connectTask && client.Connected;
            }
            catch
            {
                return false;
            }
        }
    }
}
