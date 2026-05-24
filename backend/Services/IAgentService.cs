using Orchestra.Shared.Dtos;

namespace Orchestra.Backend.Services;

public interface IAgentService
{
    Task HandleHeartbeatAsync(DeviceHeartbeatDto dto, string? remoteSourceIp = null);
    Task<List<CommandDto>> GetCommandsAsync(string deviceId);
    Task HandleCommandResultAsync(CommandResultDto result);
    Task HandleEventAsync(DeviceEventDto evt);
}
