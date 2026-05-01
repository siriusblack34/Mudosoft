using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Orchestra.Backend.Services
{
    public class ConnectionDiagnosticService
    {
        // -------------------------
        // PING TEST (1sn timeout)
        // -------------------------
        public async Task<bool> PingHost(string ip)
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(ip, 1000);
                return reply.Status == IPStatus.Success;
            }
            catch
            {
                return false;
            }
        }

        // -------------------------
        // PORT TEST (1433 default)
        // -------------------------
        public async Task<(bool success, string error)> TestPort(string ip, int port)
        {
            try
            {
                using TcpClient client = new TcpClient();
                var connectTask = client.ConnectAsync(ip, port);
                var timeoutTask = Task.Delay(1200); // 1.2 sn timeout

                var finished = await Task.WhenAny(connectTask, timeoutTask);

                if (finished == timeoutTask)
                    return (false, "Bağlantı zaman aşımına uğradı");

                if (client.Connected)
                    return (true, "");

                return (false, "Bağlantı sağlanamadı");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }
    }
}
