using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestra.Shared.Dtos;
using System.Net.Http.Json;
using System.Runtime.Versioning;

namespace Orchestra.CentralAgent.Services;

/// <summary>
/// 30 saniyede bir backend'e heartbeat göndererek cihazı kayıt altında tutar.
/// İlk heartbeatle DeviceTypeName="CentralOffice" gönderir → backend'de CentralOffice olarak işaretlenir.
/// </summary>
[SupportedOSPlatform("windows")]
public class HeartbeatService : BackgroundService
{
    private readonly ILogger<HeartbeatService> _logger;
    private readonly CentralAgentConfig _config;
    private readonly DeviceIdentityService _identity;
    private readonly HttpClient _http;

    public HeartbeatService(
        ILogger<HeartbeatService> logger,
        IOptions<CentralAgentConfig> config,
        DeviceIdentityService identity)
    {
        _logger   = logger;
        _config   = config.Value;
        _identity = identity;
        _http     = new HttpClient { BaseAddress = new Uri(_config.BackendUrl) };
    }

    private bool _vncReported = false;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var dto = BuildDto();
                await _http.PostAsJsonAsync("/api/agent/report", dto, stoppingToken);

                // İlk başarılı heartbeat'ten sonra VNC durumunu bildir
                if (!_vncReported)
                    await ReportVncStatusAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Heartbeat hatası: {Msg}", ex.Message);
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken)
                      .ContinueWith(_ => { });
        }
    }

    private async Task ReportVncStatusAsync(CancellationToken ct)
    {
        try
        {
            var vncSetup  = new VncSetupService(Microsoft.Extensions.Options.Options.Create(_config));
            bool installed = vncSetup.IsVncInstalled();
            // Şifreyi her zaman raporla — DB'de boş kalmasın
            string? password = null;
            try { password = vncSetup.GetOrGeneratePassword(); } catch { }

            var report = new
            {
                DeviceId  = _identity.GetDeviceId(),
                Installed = installed,
                Password  = password,
                Port      = 5900
            };
            var resp = await _http.PostAsJsonAsync("/api/agent/vnc-status", report, ct);
            if (resp.IsSuccessStatusCode)
            {
                // VNC gerçekten kuruluysa latch'le; değilse her heartbeat'te tekrar dene
                // (servis sonradan kalkınca şifre+durum otomatik DB'ye yansır).
                if (installed) _vncReported = true;
                _logger.LogInformation("VNC durumu bildirildi: installed={Installed}", installed);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("VNC durum bildirimi hatası: {Msg}", ex.Message);
        }
    }

    private DeviceHeartbeatDto BuildDto()
    {
        var hostname = Environment.MachineName;
        var ip       = GetLocalIp();

        return new DeviceHeartbeatDto
        {
            DeviceId        = _identity.GetDeviceId(),
            Hostname        = hostname,
            IpAddress       = ip,
            OsVersion       = Environment.OSVersion.ToString(),
            PosVersion      = "",
            SqlVersion      = "",
            CpuUsage        = 0,
            RamUsage        = 0,
            DiskUsage       = 0,
            Online          = true,
            AgentVersion    = "central-1.0",
            DeviceTypeName  = "CentralOffice",
            LastLoggedInUser = Environment.UserName,
            UptimeSince     = DateTime.UtcNow,
            Capabilities = new Orchestra.Shared.Models.AgentCapabilities
            {
                CanExecuteCommands = false,
                AgentVersion       = "central-1.0"
            }
        };
    }

    private static string GetLocalIp()
    {
        try
        {
            return System.Net.Dns.GetHostEntry(Environment.MachineName)
                .AddressList
                .FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                ?.ToString() ?? "0.0.0.0";
        }
        catch { return "0.0.0.0"; }
    }
}
