using System.Net.Sockets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Data;
using Orchestra.Backend.Hubs;
using Orchestra.Backend.Models;
using Orchestra.Backend.Services;

namespace Orchestra.Backend.Controllers;

[ApiController]
[Authorize]
[Route("api/rdp")]
public class RdpController : ControllerBase
{
    private readonly VncSessionManager _sessionManager;
    private readonly OrchestraDbContext _db;
    private readonly ConsentRequestManager _consentManager;
    private readonly IHubContext<RemoteDesktopHub> _hubContext;
    private readonly ILogger<RdpController> _logger;

    public RdpController(
        VncSessionManager sessionManager,
        OrchestraDbContext db,
        ConsentRequestManager consentManager,
        IHubContext<RemoteDesktopHub> hubContext,
        ILogger<RdpController> logger)
    {
        _sessionManager = sessionManager;
        _db = db;
        _consentManager = consentManager;
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <summary>
    /// Pre-check: is device online? Is VNC installed and reachable?
    /// Also returns VNC password for noVNC authentication.
    /// </summary>
    [HttpGet("check/{deviceId}")]
    public async Task<IActionResult> CheckDevice(string deviceId)
    {
        var device = await _db.Devices.AsNoTracking()
            .Where(d => d.Id == deviceId)
            .Select(d => new { d.IpAddress, d.RemoteSourceIp, d.Hostname, d.Online, d.VncInstalled, d.VncPassword, d.VncPort, d.Type })
            .FirstOrDefaultAsync();

        if (device == null)
        {
            return NotFound(new { error = "Device not found" });
        }

        if (string.IsNullOrEmpty(device.IpAddress))
        {
            return Ok(new
            {
                online = device.Online,
                vncReachable = false,
                vncInstalled = false,
                ipAddress = "",
                hostname = device.Hostname ?? "",
                error = "Device has no IP address"
            });
        }

        if (!device.VncInstalled)
        {
            return Ok(new
            {
                online = device.Online,
                vncReachable = false,
                vncInstalled = false,
                ipAddress = device.IpAddress,
                hostname = device.Hostname ?? "",
                activeSessionCount = _sessionManager.ActiveSessionCount,
                maxConnections = _sessionManager.MaxConnections,
                error = "VNC kurulu değil. Önce VNC kurulumunu başlatın."
            });
        }

        // TCP test to VNC port. Önce self-report IP'yi dene; multi-NIC laptop'larda agent
        // yanlış NIC seçebiliyor (VPN/Hyper-V virtual switch). Fail olursa heartbeat'ten gelen
        // gerçek RemoteSourceIp ile yeniden dene.
        var vncPort = device.VncPort > 0 ? device.VncPort : 5900;
        bool vncReachable = false;
        string reachableIp = device.IpAddress;

        async Task<bool> TryConnectAsync(string host)
        {
            try
            {
                using var tcp = new TcpClient();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await tcp.ConnectAsync(host, vncPort, cts.Token);
                return true;
            }
            catch { return false; }
        }

        vncReachable = await TryConnectAsync(device.IpAddress);

        if (!vncReachable
            && !string.IsNullOrWhiteSpace(device.RemoteSourceIp)
            && device.RemoteSourceIp != device.IpAddress)
        {
            if (await TryConnectAsync(device.RemoteSourceIp))
            {
                vncReachable = true;
                reachableIp = device.RemoteSourceIp;
            }
        }

        bool requiresConsent = device.Type == DeviceType.CentralOffice;
        bool deviceBusy = _sessionManager.HasActiveSessionForDevice(deviceId);

        return Ok(new
        {
            online = device.Online,
            vncReachable,
            vncInstalled = true,
            ipAddress = reachableIp,
            hostname = device.Hostname ?? "",
            // 🔒 SECURITY (Y-6): VNC parolası artık client'a DÖNMÜYOR. /ws/vnc proxy'si parolayı
            // sunucu tarafında DB'den okur; frontend bu değeri hiç kullanmıyordu.
            activeSessionCount = _sessionManager.ActiveSessionCount,
            maxConnections = _sessionManager.MaxConnections,
            activeSessionCountForDevice = _sessionManager.GetSessionCountForDevice(deviceId),
            requiresConsent,
            deviceBusy
        });
    }

    /// <summary>
    /// Lightweight ping — used by frontend to measure round-trip latency.
    /// </summary>
    [HttpGet("ping")]
    public IActionResult Ping() => Ok(new { ok = true });

    /// <summary>
    /// Session audit log — who connected to which device and when.
    /// </summary>
    [HttpGet("logs")]
    public async Task<IActionResult> GetSessionLogs(
        [FromQuery] string? deviceId,
        [FromQuery] int limit = 100)
    {
        var query = _db.VncSessionLogs.AsNoTracking()
            .OrderByDescending(l => l.StartedAtUtc);

        IQueryable<Orchestra.Backend.Models.VncSessionLog> filtered = string.IsNullOrEmpty(deviceId)
            ? query
            : query.Where(l => l.DeviceId == deviceId);

        var logs = await filtered.Take(limit).Select(l => new
        {
            l.Id,
            l.SessionId,
            l.DeviceId,
            l.Username,
            l.TargetIp,
            l.StartedAtUtc,
            l.EndedAtUtc,
            l.DurationSeconds,
            l.DisconnectReason
        }).ToListAsync();

        return Ok(logs);
    }

    /// <summary>
    /// List active VNC proxy sessions (admin visibility).
    /// </summary>
    [HttpGet("sessions")]
    public IActionResult GetActiveSessions()
    {
        var sessions = _sessionManager.GetActiveSessions().Select(s => new
        {
            s.SessionId,
            s.DeviceId,
            s.Username,
            s.TargetIp,
            s.StartedAt,
            durationMinutes = (int)(DateTime.UtcNow - s.StartedAt).TotalMinutes
        });

        return Ok(sessions);
    }

    /// <summary>
    /// Force-disconnect an active session.
    /// </summary>
    [HttpDelete("sessions/{sessionId}")]
    public IActionResult TerminateSession(string sessionId)
    {
        if (!_sessionManager.TryGetSession(sessionId, out _))
        {
            return NotFound(new { error = "Session not found" });
        }

        _sessionManager.RemoveSession(sessionId);
        return Ok(new { message = "Session terminated" });
    }

    /// <summary>
    /// Merkez cihazı için kullanıcıya onay isteği gönderir.
    /// Agent'a hub üzerinden RequestConsent mesajı iletilir; sonuç polling ile alınır.
    /// </summary>
    [HttpPost("consent-request/{deviceId}")]
    public async Task<IActionResult> CreateConsentRequest(string deviceId)
    {
        var device = await _db.Devices.AsNoTracking()
            .Where(d => d.Id == deviceId)
            .Select(d => new { d.Type, d.Online })
            .FirstOrDefaultAsync();

        if (device == null)
            return NotFound(new { error = "Cihaz bulunamadı" });

        if (device.Type != DeviceType.CentralOffice)
            return BadRequest(new { error = "Bu cihaz onay gerektirmiyor" });

        if (!device.Online)
            return BadRequest(new { error = "Cihaz çevrimdışı" });

        var requesterUsername = User.Identity?.Name ?? "Bilinmeyen";
        var requesterName     = User.FindFirst("name")?.Value
                             ?? User.FindFirst(System.Security.Claims.ClaimTypes.GivenName)?.Value
                             ?? requesterUsername;

        // Önceki bekleyen istekleri iptal et — böylece eski diyaloglar kapanır
        _consentManager.CancelPending(deviceId);
        await _hubContext.Clients.Group($"Device_{deviceId}")
            .SendAsync("CancelConsent");

        var requestId = _consentManager.CreateRequest(deviceId, requesterName, requesterUsername);

        _logger.LogInformation("[Consent] Sending RequestConsent to Device_{DeviceId} — requestId={RequestId} requester={Requester}", deviceId, requestId, requesterName);

        // Agent'a hub üzerinden gönder
        await _hubContext.Clients.Group($"Device_{deviceId}")
            .SendAsync("RequestConsent", requestId, requesterName, requesterUsername);

        _logger.LogInformation("[Consent] RequestConsent sent — requestId={RequestId}", requestId);

        return Ok(new { requestId });
    }

    /// <summary>
    /// Frontend'in onay durumunu sorgulayacağı polling endpoint'i.
    /// </summary>
    [HttpGet("consent-status/{requestId}")]
    public IActionResult GetConsentStatus(string requestId)
    {
        var status = _consentManager.GetStatus(requestId);
        return Ok(status);
    }

    /// <summary>
    /// HTTP üzerinden doğrudan onay cevabı — SignalR bağlantısı geçici olarak koptuğunda bile çalışır.
    /// requestId tahmin edilemez 32-karakter hex olduğundan ayrı auth gerekmez.
    /// </summary>
    [HttpPost("consent-response/{requestId}")]
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    public IActionResult SubmitConsentResponseHttp(string requestId, [FromQuery] bool approved, [FromQuery] string? denyReason = null)
    {
        _logger.LogInformation("🔐 HTTP ConsentResponse: requestId={RequestId} approved={Approved}", requestId, approved);
        _consentManager.Resolve(requestId, approved, denyReason);
        return Ok(new { ok = true });
    }
}
