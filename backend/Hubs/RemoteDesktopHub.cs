using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using System.Collections.Concurrent;

using Orchestra.Shared.Dtos;
using Orchestra.Backend.Services;

namespace Orchestra.Backend.Hubs;

// 🔒 SECURITY: Hub bağlantısı anonim kalır ki token'sız AGENT'lar (yayını üreten taraf) RegisterDevice
// yapabilsin. VIEWER (operatör) tarafından çağrılan tehlikeli metotlar metot-içi EnsureViewerAuthenticated()
// ile korunur. Bu attribute olmadan global FallbackPolicy hub bağlantısını kilitler ve filoyu bricklerdi.
[AllowAnonymous]
public class RemoteDesktopHub : Hub
{
    private readonly ILogger<RemoteDesktopHub> _logger;
    private readonly ConsentRequestManager _consentManager;

    public RemoteDesktopHub(ILogger<RemoteDesktopHub> logger, ConsentRequestManager consentManager)
    {
        _logger = logger;
        _consentManager = consentManager;
    }

    // 🔒 SECURITY (K-1): Viewer (frontend operator) tarafından çağrılan metotlar kimlik doğrulaması ister.
    // Frontend token'ı accessTokenFactory ile gönderir → Context.User dolu olur. Agent tarafı (RegisterDevice,
    // StreamFrame, SendAnswer, SendIceCandidate) token'sız bağlandığı için bu kapıdan geçmez; onlar yayını
    // ÜRETEN taraf. Kimlik doğrulamasız bir saldırgan artık ekran izleyemez / input enjekte edemez.
    private void EnsureViewerAuthenticated([System.Runtime.CompilerServices.CallerMemberName] string method = "")
    {
        if (Context.User?.Identity?.IsAuthenticated != true)
        {
            _logger.LogWarning("🚫 Unauthorized hub call '{Method}' from {ConnectionId}", method, Context.ConnectionId);
            throw new HubException("Unauthorized");
        }
    }

    // ... (State management)

    // Key: DeviceId, Value: Agent ConnectionId
    private static readonly ConcurrentDictionary<string, string> _deviceAgentConnections = new();

    public async Task SendInput(string deviceId, InputEventDto input)
    {
        EnsureViewerAuthenticated();
        _logger.LogInformation("📥 SendInput called for device {DeviceId}, Type: {Type}", deviceId, input.Type);
        // 1. Cihazın ConnectionId'sini bul
        if (_deviceAgentConnections.TryGetValue(deviceId, out var connectionId))
        {
            // 2. Agente ilet
            await Clients.Client(connectionId).SendAsync("PerformInput", input);
        }
        else
        {
            _logger.LogWarning("⚠️ Device {DeviceId} not found in connections", deviceId);
        }
    }

    // Hangi DeviceId kimler tarafından izleniyor?
    // Key: DeviceId, Value: List of Viewer ConnectionIds
    private static readonly ConcurrentDictionary<string, List<string>> _deviceViewers = new();

    // Hangi ConnectionId hangi Device'a ait? (Agent bağlandığında)
    private static readonly ConcurrentDictionary<string, string> _connectionToDevice = new();

    public override Task OnConnectedAsync()
    {
        _logger.LogInformation("🔗 RemoteDesktopHub: Client connected - {ConnectionId}", Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // Temizlik işlemleri
        var connectionId = Context.ConnectionId;

        // Eğer kopan bir Viewer ise:
        foreach (var deviceId in _deviceViewers.Keys)
        {
            if (_deviceViewers.TryGetValue(deviceId, out var viewers))
            {
                lock (viewers)
                {
                    viewers.Remove(connectionId);
                }
            }
        }
        
        // Eğer kopan bir Agent ise:
        if (_connectionToDevice.TryRemove(connectionId, out var agentDeviceId))
        {
             _deviceAgentConnections.TryRemove(agentDeviceId, out _);
        }

        await base.OnDisconnectedAsync(exception);
    }

    // 1. Viewer çağırır: "Ben şu cihazı izlemek istiyorum"
    public async Task JoinSession(string deviceId)
    {
        EnsureViewerAuthenticated();
        var connectionId = Context.ConnectionId;
        _logger.LogInformation("👁️ JoinSession: Viewer {ConnectionId} wants to watch device {DeviceId}", connectionId, deviceId);
        
        _deviceViewers.AddOrUpdate(deviceId, 
            new List<string> { connectionId }, 
            (key, list) => {
                lock(list) { list.Add(connectionId); }
                return list;
            });

        // Varsa o odaya (Group) ekleyelim, böylece tek tek loop yerine Group'a atarız
        await Groups.AddToGroupAsync(connectionId, $"AvailableViewers_{deviceId}");

        // Agent'a (eğer bağlıysa) "Yayın Başla" emri gönderilebilir
        var agentConnected = _deviceAgentConnections.ContainsKey(deviceId);
        _logger.LogInformation("📡 JoinSession: Agent connected for {DeviceId}: {Connected}", deviceId, agentConnected);
        
        await Clients.OthersInGroup($"Device_{deviceId}").SendAsync("StartStreaming");
        _logger.LogInformation("📝 JoinSession: Sent StartStreaming to Device_{DeviceId} group", deviceId);
    }

    // 2. Viewer çağırır: "İzlemeyi bıraktım"
    public async Task LeaveSession(string deviceId)
    {
        EnsureViewerAuthenticated();
        var connectionId = Context.ConnectionId;
        await Groups.RemoveFromGroupAsync(connectionId, $"AvailableViewers_{deviceId}");
        
        // Listeden çıkar
        if (_deviceViewers.TryGetValue(deviceId, out var viewers))
        {
            lock(viewers) { viewers.Remove(connectionId); }
        }
    }

    // 3. Agent çağırır: "Ben bu DeviceId'yim, yayına hazırım"
    public async Task RegisterDevice(string deviceId)
    {
        var connectionId = Context.ConnectionId;
        _logger.LogInformation("🖥️ RegisterDevice: Device {DeviceId} registered with connection {ConnectionId}", deviceId, connectionId);
        _connectionToDevice[connectionId] = deviceId;
        _deviceAgentConnections[deviceId] = connectionId;
        await Groups.AddToGroupAsync(connectionId, $"Device_{deviceId}");
        _logger.LogInformation("✅ RegisterDevice: {DeviceId} added to group Device_{DeviceId}", deviceId, deviceId);
    }

    // 4. Agent çağırır: "Benden Görüntü Paketi (Frame)" - Legacy, will be removed after WebRTC migration
    private static int _frameCount = 0;
    public async Task StreamFrame(string deviceId, byte[] imageContent)
    {
        _frameCount++;
        if (_frameCount % 30 == 1) // Log every 30 frames
        {
            _logger.LogInformation("🖼️ StreamFrame #{Count}: Device {DeviceId}, Size: {Size} bytes", _frameCount, deviceId, imageContent.Length);
        }
        
        // Convert bytes to base64 for JavaScript client
        var base64 = Convert.ToBase64String(imageContent);
        
        // Bu cihaza abone olan (JoinSession yapmış) herkese gönder
        await Clients.Group($"AvailableViewers_{deviceId}").SendAsync("ReceiveFrame", base64);
    }
    
    // 5. Viewer çağırır: "Şu monitörü seç" (-1 = tümü, 0+ = tek monitör)
    public async Task SelectMonitor(string deviceId, int monitorIndex)
    {
        EnsureViewerAuthenticated();
        _logger.LogInformation("🖥️ SelectMonitor: Device {DeviceId} switching to monitor {Monitor}", deviceId, monitorIndex);
        
        if (_deviceAgentConnections.TryGetValue(deviceId, out var agentConnectionId))
        {
            await Clients.Client(agentConnectionId).SendAsync("SelectMonitor", monitorIndex);
        }
        else
        {
            _logger.LogWarning("⚠️ SelectMonitor: Device {DeviceId} not connected", deviceId);
        }
    }
    
    #region Consent (Merkez Cihaz Onay Akışı)

    /// <summary>
    /// Agent çağırır: kullanıcının verdiği onay yanıtını iletir.
    /// </summary>
    public Task SubmitConsentResponse(string requestId, bool approved, string? denyReason = null)
    {
        _logger.LogInformation("🔐 SubmitConsentResponse: requestId={RequestId} approved={Approved} reason={Reason}", requestId, approved, denyReason);
        _consentManager.Resolve(requestId, approved, denyReason);
        return Task.CompletedTask;
    }

    #endregion

    #region Session Overlay (Tüm Cihazlar)

    /// <summary>
    /// Frontend çağırır: bağlantı kurulduğunda agent overlay'i göstersin.
    /// </summary>
    public async Task ShowConnectionOverlay(string deviceId, string technicianName, string technicianIp)
    {
        EnsureViewerAuthenticated();
        _logger.LogInformation("📺 ShowConnectionOverlay: device={DeviceId} tech={Tech}", deviceId, technicianName);
        if (_deviceAgentConnections.TryGetValue(deviceId, out var agentConnId))
            await Clients.Client(agentConnId).SendAsync("ShowOverlay", technicianName, technicianIp);
    }

    /// <summary>
    /// Frontend çağırır: bağlantı kesildiğinde agent overlay'i gizlesin.
    /// </summary>
    public async Task HideConnectionOverlay(string deviceId)
    {
        EnsureViewerAuthenticated();
        _logger.LogInformation("📺 HideConnectionOverlay: device={DeviceId}", deviceId);
        if (_deviceAgentConnections.TryGetValue(deviceId, out var agentConnId))
            await Clients.Client(agentConnId).SendAsync("HideOverlay");
    }

    #endregion

    #region Input Lock (Mouse/Klavye Kilidi)

    /// <summary>
    /// Frontend çağırır: hedef cihazda kullanıcı girişini kilitler / açar.
    /// </summary>
    public async Task SetInputLock(string deviceId, bool locked)
    {
        EnsureViewerAuthenticated();
        _logger.LogInformation("🔒 SetInputLock: device={DeviceId} locked={Locked}", deviceId, locked);
        if (_deviceAgentConnections.TryGetValue(deviceId, out var agentConnId))
            await Clients.Client(agentConnId).SendAsync("SetInputLock", locked);
    }

    #endregion

    #region WebRTC Signaling
    
    // Viewer -> Agent: WebRTC Offer gönder
    public async Task SendOffer(string deviceId, string sdp)
    {
        EnsureViewerAuthenticated();
        _logger.LogInformation("📤 SendOffer: Viewer sending offer to device {DeviceId}", deviceId);
        
        if (_deviceAgentConnections.TryGetValue(deviceId, out var agentConnectionId))
        {
            await Clients.Client(agentConnectionId).SendAsync("ReceiveOffer", sdp);
            _logger.LogInformation("✅ Offer forwarded to agent");
        }
        else
        {
            _logger.LogWarning("⚠️ SendOffer: Device {DeviceId} not connected", deviceId);
        }
    }
    
    // Agent -> Viewer: WebRTC Answer gönder
    public async Task SendAnswer(string deviceId, string sdp)
    {
        _logger.LogInformation("📤 SendAnswer: Agent sending answer for device {DeviceId}", deviceId);
        
        // İlgili viewer'lara gönder
        await Clients.Group($"AvailableViewers_{deviceId}").SendAsync("ReceiveAnswer", sdp);
        _logger.LogInformation("✅ Answer forwarded to viewers");
    }
    
    // ICE Candidate exchange (both directions)
    public async Task SendIceCandidate(string deviceId, string candidateJson)
    {
        var connectionId = Context.ConnectionId;
        
        // Bu bağlantı agent mı viewer mı?
        if (_connectionToDevice.TryGetValue(connectionId, out var agentDeviceId) && agentDeviceId == deviceId)
        {
            // Agent'tan geliyor -> Viewer'lara gönder
            _logger.LogDebug("🧊 ICE from Agent -> Viewers for {DeviceId}", deviceId);
            await Clients.Group($"AvailableViewers_{deviceId}").SendAsync("ReceiveIceCandidate", candidateJson);
        }
        else
        {
            // Viewer'dan geliyor -> Agent'a gönder
            _logger.LogDebug("🧊 ICE from Viewer -> Agent for {DeviceId}", deviceId);
            if (_deviceAgentConnections.TryGetValue(deviceId, out var agentConnectionId))
            {
                await Clients.Client(agentConnectionId).SendAsync("ReceiveIceCandidate", candidateJson);
            }
        }
    }
    
    #endregion
}

