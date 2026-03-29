using System.Net.Sockets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MudoSoft.Backend.Data;
using MudoSoft.Backend.Services;

namespace MudoSoft.Backend.Controllers;

[ApiController]
[Authorize]
[Route("api/rdp")]
public class RdpController : ControllerBase
{
    private readonly VncSessionManager _sessionManager;
    private readonly MudoSoftDbContext _db;

    public RdpController(
        VncSessionManager sessionManager,
        MudoSoftDbContext db)
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
            .Select(d => new { d.IpAddress, d.Hostname, d.Online, d.VncInstalled, d.VncPassword, d.VncPort })
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

        // TCP test to VNC port
        var vncPort = device.VncPort > 0 ? device.VncPort : 5900;
        bool vncReachable = false;
        try
        {
            using var tcp = new TcpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await tcp.ConnectAsync(device.IpAddress, vncPort, cts.Token);
            vncReachable = true;
        }
        catch { }

        return Ok(new
        {
            online = device.Online,
            vncReachable,
            vncInstalled = true,
            ipAddress = device.IpAddress,
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

        IQueryable<MudoSoft.Backend.Models.VncSessionLog> filtered = string.IsNullOrEmpty(deviceId)
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
