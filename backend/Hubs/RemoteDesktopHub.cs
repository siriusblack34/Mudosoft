using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

using Mudosoft.Shared.Dtos; // EKLENDİ

namespace MudoSoft.Backend.Hubs;

public class RemoteDesktopHub : Hub
{
    private readonly ILogger<RemoteDesktopHub> _logger;
    
    public RemoteDesktopHub(ILogger<RemoteDesktopHub> logger)
    {
        _logger = logger;
    }
    
    // ... (State management)

    // Key: DeviceId, Value: Agent ConnectionId
    private static readonly ConcurrentDictionary<string, string> _deviceAgentConnections = new();

    public async Task SendInput(string deviceId, InputEventDto input)
    {
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
    
    #region WebRTC Signaling
    
    // Viewer -> Agent: WebRTC Offer gönder
    public async Task SendOffer(string deviceId, string sdp)
    {
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

