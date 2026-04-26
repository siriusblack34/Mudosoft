using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace MudoSoft.Backend.Services
{
    public class FastSqlReachabilityService
    {
        public async Task<bool> IsSqlReachableAsync(string ip, int port = 1433, int timeoutMs = 1500)
        {
            if (string.IsNullOrWhiteSpace(ip))
                return false;

            if (await TryTcpConnectAsync(ip, port, timeoutMs))
                return true;

            // Tek SYN kaybi yuzunden "kapali" raporlamayi onle: ping'deki gibi kisa retry
            await Task.Delay(100);
            return await TryTcpConnectAsync(ip, port, timeoutMs);
        }

        private static async Task<bool> TryTcpConnectAsync(string ip, int port, int timeoutMs)
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            using var client = new TcpClient();
            try
            {
                await client.ConnectAsync(ip, port, cts.Token);
                return client.Connected;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> IsPingReachableAsync(string ip, int timeoutMs = 1500)
        {
            if (string.IsNullOrWhiteSpace(ip))
                return false;

            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(ip, timeoutMs);
                if (reply.Status == IPStatus.Success)
                    return true;
            }
            catch { }

            // ICMP başarısızsa kısa bekle ve tekrar dene
            try
            {
                await Task.Delay(100);
                using var ping2 = new Ping();
                var reply2 = await ping2.SendPingAsync(ip, timeoutMs);
                return reply2.Status == IPStatus.Success;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Router erişim kontrolü: ICMP ping + TCP fallback (port 80/443).
        /// ICMP başarısız olsa bile router web arayüzü açıksa online kabul et.
        /// </summary>
        public async Task<bool> IsRouterReachableAsync(string ip, int timeoutMs = 800)
        {
            var (ok, _) = await PingRouterWithLatencyAsync(ip, timeoutMs);
            return ok;
        }

        /// <summary>
        /// Router icin ping + latency olcumu. Karasal / mobil (4.5G) hat tespiti icin kullanilir.
        /// ICMP basarili ise gercek RTT donulur. ICMP basarisiz ise TCP 80/443 fallback denenir
        /// (rtt olcumu stopwatch ile yaklasik).
        /// </summary>
        public async Task<(bool ok, int? rttMs)> PingRouterWithLatencyAsync(string ip, int timeoutMs = 800)
        {
            if (string.IsNullOrWhiteSpace(ip))
                return (false, null);

            // 1) ICMP ping — gercek RTT
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(ip, timeoutMs);
                if (reply.Status == IPStatus.Success)
                    return (true, (int)reply.RoundtripTime);
            }
            catch { }

            // Tek SYN kaybina karsi kisa retry
            try
            {
                await Task.Delay(80);
                using var ping2 = new Ping();
                var reply2 = await ping2.SendPingAsync(ip, timeoutMs);
                if (reply2.Status == IPStatus.Success)
                    return (true, (int)reply2.RoundtripTime);
            }
            catch { }

            // 2) TCP fallback — router web arayuzu acik olabilir (ICMP bloke)
            // DIKKAT: TCP handshake RTT'si gercek ping latency'sini yansitmaz (cok dusuk cikar).
            // Online/offline kararina katkisi var ama hat tipi tespiti icin RttMs null kalir.
            var httpTask = IsTcpPortOpenAsync(ip, 80, timeoutMs);
            var httpsTask = IsTcpPortOpenAsync(ip, 443, timeoutMs);
            await Task.WhenAll(httpTask, httpsTask);

            if (httpTask.Result || httpsTask.Result)
                return (true, null);

            return (false, null);
        }

        private static Task<bool> IsTcpPortOpenAsync(string ip, int port, int timeoutMs)
            => TryTcpConnectAsync(ip, port, timeoutMs);

        /// <summary>
        /// PC/Kasa icin paralel coklu kontrol: Ping + SQL (1433)
        /// Her ikisi ayni anda yapilir, toplam sure en yavas olanla sinirli kalir.
        /// </summary>
        public async Task<(bool ping, bool sql)> CheckDeviceMultiAsync(string ip, int timeoutMs = 1500)
        {
            if (string.IsNullOrWhiteSpace(ip))
                return (false, false);

            var pingTask = IsPingReachableAsync(ip, timeoutMs);
            var sqlTask = IsSqlReachableAsync(ip, 1433, timeoutMs);

            await Task.WhenAll(pingTask, sqlTask);

            return (pingTask.Result, sqlTask.Result);
        }
    }
}
