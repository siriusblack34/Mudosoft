using System.Collections.Concurrent;
using System.Net.Sockets;

namespace MudoSoft.Backend.Services;

/// <summary>
/// Manages VNC proxy sessions (raw TCP connections to VNC servers).
/// Tracks active sessions and enforces connection limits.
/// </summary>
public class VncSessionManager
{
    private readonly ConcurrentDictionary<string, VncSession> _sessions = new();
    private readonly IConfiguration _config;
    private readonly ILogger<VncSessionManager> _logger;

    public VncSessionManager(IConfiguration config, ILogger<VncSessionManager> logger)
    {
        _config = config;
        _logger = logger;
    }

    public int ActiveSessionCount => _sessions.Count;
    public int MaxConnections => _config.GetValue("Guacamole:MaxConnections", 10);

    public IEnumerable<VncSession> GetActiveSessions() => _sessions.Values;

    /// <summary>
    /// Open a raw TCP connection to the target VNC server.
    /// No protocol handshake — just a TCP pipe for noVNC to use.
    /// </summary>
    public async Task<VncSession> CreateSessionAsync(
        string deviceId,
        string targetIp,
        string jwtUsername,
        int vncPort,
        CancellationToken ct = default)
    {
        if (_sessions.Count >= MaxConnections)
            throw new InvalidOperationException($"Maksimum eşzamanlı oturum sayısına ({MaxConnections}) ulaşıldı.");

        var sessionId = Guid.NewGuid().ToString("N");
        TcpClient? tcp = null;
        NetworkStream? stream = null;

        try
        {
            tcp = new TcpClient();
            tcp.NoDelay = true;
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(TimeSpan.FromSeconds(10));
            await tcp.ConnectAsync(targetIp, vncPort, connectCts.Token);
            stream = tcp.GetStream();

            _logger.LogInformation("[VNC-Proxy] Connected to VNC at {Ip}:{Port} for device {DeviceId} by {User}",
                targetIp, vncPort, deviceId, jwtUsername);

            var session = new VncSession
            {
                SessionId = sessionId,
                DeviceId = deviceId,
                Username = jwtUsername,
                TargetIp = targetIp,
                TcpClient = tcp,
                TcpStream = stream,
                StartedAt = DateTime.UtcNow
            };

            _sessions.TryAdd(sessionId, session);
            return session;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[VNC-Proxy] Failed to connect to VNC at {Ip}:{Port}", targetIp, vncPort);
            stream?.Dispose();
            tcp?.Dispose();
            throw;
        }
    }

    public void RemoveSession(string sessionId)
    {
        if (_sessions.TryRemove(sessionId, out var session))
        {
            _logger.LogInformation("[VNC-Proxy] Session {SessionId} ended for device {DeviceId} (duration: {Duration})",
                sessionId, session.DeviceId, DateTime.UtcNow - session.StartedAt);
            try { session.TcpStream?.Dispose(); } catch { }
            try { session.TcpClient?.Dispose(); } catch { }
        }
    }

    public bool TryGetSession(string sessionId, out VncSession? session)
    {
        return _sessions.TryGetValue(sessionId, out session);
    }
}

public class VncSession
{
    public string SessionId { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string Username { get; set; } = "";
    public string TargetIp { get; set; } = "";
    public TcpClient TcpClient { get; set; } = null!;
    public NetworkStream TcpStream { get; set; } = null!;
    public DateTime StartedAt { get; set; }
}
