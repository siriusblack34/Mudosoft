using Mudosoft.Shared.Dtos;
using MudoSoft.Backend.Data;

namespace MudoSoft.Backend.Services;

public class AgentService : IAgentService
{
    private readonly CommandQueue _queue;
    private readonly ILogger<AgentService> _logger;

    public AgentService(CommandQueue queue, ILogger<AgentService> logger)
    {
        _queue = queue;
        _logger = logger;
    }

    public Task HandleHeartbeatAsync(DeviceHeartbeatDto dto)
    {
        _logger.LogInformation("Heartbeat from {DeviceId} CPU:{Cpu} RAM:{Ram} DISK:{Disk}",
            dto.DeviceId, dto.CpuUsage, dto.RamUsage, dto.DiskUsage);

        return Task.CompletedTask;
    }

    public Task<List<CommandDto>> GetCommandsAsync(string deviceId)
    {
        var cmds = _queue.DequeueByDevice(deviceId);
        return Task.FromResult(cmds);
    }

    public Task HandleCommandResultAsync(CommandResultDto result)
    {
        _logger.LogInformation("Command {CommandId} executed by {DeviceId} Success:{Success}",
            result.CommandId, result.DeviceId, result.Success);

        return Task.CompletedTask;
    }

    public Task HandleEventAsync(DeviceEventDto evt)
    {
        _logger.LogWarning("Event from {DeviceId}: {Type} ({Severity}) {Details}",
            evt.DeviceId, evt.EventType, evt.Severity, evt.Details);

        return Task.CompletedTask;
    }
}
