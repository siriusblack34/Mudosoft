using Mudosoft.Shared.Dtos;
using System.Collections.Concurrent;

namespace MudoSoft.Backend.Data;

public class CommandQueue
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<CommandDto>> _queues = new();

    public void Enqueue(CommandDto cmd)
    {
        var queue = _queues.GetOrAdd(cmd.DeviceId, _ => new ConcurrentQueue<CommandDto>());
        queue.Enqueue(cmd);
    }

    public List<CommandDto> DequeueByDevice(string deviceId)
    {
        if (!_queues.TryGetValue(deviceId, out var queue))
            return new List<CommandDto>();

        var cmds = new List<CommandDto>();
        while (queue.TryDequeue(out var cmd))
            cmds.Add(cmd);

        return cmds;
    }
}
