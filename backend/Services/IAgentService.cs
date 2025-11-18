using Mudosoft.Shared.Dtos;

namespace MudoSoft.Backend.Services;

public interface IAgentService
{
    Task HandleHeartbeatAsync(DeviceHeartbeatDto dto);
    Task<List<CommandDto>> GetCommandsAsync(string deviceId);
    Task HandleCommandResultAsync(CommandResultDto result);
    Task HandleEventAsync(DeviceEventDto evt);
}
