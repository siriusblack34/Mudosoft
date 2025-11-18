using Mudosoft.Shared.Dtos;

namespace MudoSoft.Backend.Data;

public class CommandQueue
{
    private readonly List<CommandDto> _commands = new();

    public void Enqueue(CommandDto cmd) => _commands.Add(cmd);

    public List<CommandDto> DequeueByDevice(string deviceId)
    {
        var cmds = _commands.Where(x => x.DeviceId == deviceId).ToList();
        _commands.RemoveAll(x => x.DeviceId == deviceId);
        return cmds;
    }
}
