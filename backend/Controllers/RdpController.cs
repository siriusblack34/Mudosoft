using System.Net.Sockets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Data;
using Orchestra.Backend.Services;

namespace Orchestra.Backend.Controllers;

[ApiController]
[Authorize]
[Route("api/rdp")]
public class RdpController : ControllerBase
{
    private readonly VncSessionManager _sessionManager;
    private readonly OrchestraDbContext _db;

    public RdpController(
        VncSessionManager sessionManager,
        OrchestraDbContext db)
    {
        _sessionManager = sessionManager;
        _db = db;
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
            .Select(d => new { d.IpAddress, d.RemoteSourceIp, d.Hostname, d.Online, d.VncInstalled, d.VncPassword, d.VncPort })
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

        return Ok(new
        {
            online = device.Online,
            vncReachable,
            vncInstalled = true,
            ipAddress = reachableIp,
            hostname = device.Hostname ?? "",
            vncPassword = device.VncPassword,
            activeSessionCount = _sessionManager.ActiveSessionCount,
            maxConnections = _sessionManager.MaxConnections
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
}
