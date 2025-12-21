using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

using Mudosoft.Shared.Dtos; // EKLENDİ

namespace MudoSoft.Backend.Hubs;

public class RemoteDesktopHub : Hub
{
    // ... (State management)

    // Key: DeviceId, Value: Agent ConnectionId
    private static readonly ConcurrentDictionary<string, string> _deviceAgentConnections = new();

    public async Task SendInput(string deviceId, InputEventDto input)
    {
        // 1. Cihazın ConnectionId'sini bul
        if (_deviceAgentConnections.TryGetValue(deviceId, out var connectionId))
        {
            // 2. Agente ilet
            await Clients.Client(connectionId).SendAsync("PerformInput", input);
        }
    }

    // Hangi DeviceId kimler tarafından izleniyor?
    // Key: DeviceId, Value: List of Viewer ConnectionIds
    private static readonly ConcurrentDictionary<string, List<string>> _deviceViewers = new();

    // Hangi ConnectionId hangi Device'a ait? (Agent bağlandığında)
    private static readonly ConcurrentDictionary<string, string> _connectionToDevice = new();

    public override Task OnConnectedAsync()
    {
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
        
        _deviceViewers.AddOrUpdate(deviceId, 
            new List<string> { connectionId }, 
            (key, list) => {
                lock(list) { list.Add(connectionId); }
                return list;
            });

        // Varsa o odaya (Group) ekleyelim, böylece tek tek loop yerine Group'a atarız
        await Groups.AddToGroupAsync(connectionId, $"AvailableViewers_{deviceId}");

        // Agent'a (eğer bağlıysa) "Yayın Başla" emri gönderilebilir
        // Bunu ileride yapabiliriz, şu an Agent sürekli yayın yapacak veya Action ile tetiklenecek
        await Clients.OthersInGroup($"Device_{deviceId}").SendAsync("StartStreaming");
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
        _connectionToDevice[connectionId] = deviceId;
        _deviceAgentConnections[deviceId] = connectionId; // MAPLE
        await Groups.AddToGroupAsync(connectionId, $"Device_{deviceId}");
    }

    // 4. Agent çağırır: "Benden Görüntü Paketi (Frame)"
    public async Task StreamFrame(string deviceId, byte[] imageContent)
    {
        // Bu cihaza abone olan (JoinSession yapmış) herkese gönder
        // 1. Yöntem: Group
        await Clients.Group($"AvailableViewers_{deviceId}").SendAsync("ReceiveFrame", imageContent);
        
        // VEYA 2. Yöntem: Dictionary'den bulup gönder (Grup daha performanslıdır)
    }
}
