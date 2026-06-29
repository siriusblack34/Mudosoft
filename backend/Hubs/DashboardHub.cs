using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;

namespace Orchestra.Backend.Hubs
{
    [AllowAnonymous] // Allow Agents to connect without JWT
    public class DashboardHub : Hub
    {
        private readonly ILogger<DashboardHub> _logger;

        public DashboardHub(ILogger<DashboardHub> logger)
        {
            _logger = logger;
        }

        public override async Task OnConnectedAsync()
        {
            // Log connection (User or Agent)
            var userId = Context.User?.Identity?.Name;
            var deviceId = Context.GetHttpContext()?.Request.Query["deviceId"].ToString();

            if (!string.IsNullOrEmpty(deviceId))
            {
                // It's an Agent
                await Groups.AddToGroupAsync(Context.ConnectionId, "Agents");
                _logger.LogInformation($"✅ Agent Connected: {deviceId} ({Context.ConnectionId})");
            }
            else
            {
                // It's an Admin/User — 🔒 SECURITY (Y-4): yalnızca kimliği doğrulanmış kullanıcı "Admins"
                // grubuna girip tüm filo telemetrisini alabilir. Anonim (token'sız, deviceId'siz) bağlantı
                // eskiden doğrudan "Admins" grubuna düşüyordu → bilgi sızıntısı. Artık reddediliyor.
                if (Context.User?.Identity?.IsAuthenticated != true)
                {
                    _logger.LogWarning($"🚫 Unauthenticated dashboard connection rejected ({Context.ConnectionId})");
                    Context.Abort();
                    return;
                }
                await Groups.AddToGroupAsync(Context.ConnectionId, "Admins");
                _logger.LogInformation($"👤 Admin Connected: {userId} ({Context.ConnectionId})");
            }

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var deviceId = Context.GetHttpContext()?.Request.Query["deviceId"].ToString();
            if (!string.IsNullOrEmpty(deviceId))
            {
                _logger.LogWarning($"❌ Agent Disconnected: {deviceId}");
            }
            if (exception != null)
            {
                _logger.LogError($"Connection Error: {exception.Message}");
            }
            await base.OnDisconnectedAsync(exception);
        }

        // Method called by Agents to send live stats
        // Agents must join with ?deviceId=XYZ and Authorization: Bearer TOKEN
        public async Task SendTelemetry(string deviceId, double cpuUsage, double ramUsage, double diskUsage)
        {
            // Broadcast to all "Admins" group members
            await Clients.Group("Admins").SendAsync("ReceiveTelemetry", new 
            {
                DeviceId = deviceId,
                CpuUsage = cpuUsage,
                RamUsage = ramUsage,
                DiskUsage = diskUsage,
                Timestamp = DateTime.UtcNow
            });
        }
    }
}
