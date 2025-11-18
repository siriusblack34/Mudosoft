namespace Mudosoft.Shared.Enums;

public enum CommandType
{
    Reboot = 1,
    Shutdown = 2,
    RestartService = 3,
    RunPowerShell = 4,
    RunBatch = 5,
    CopyFile = 6,
    CustomPosMaintenance = 100 // POS özel komutlar için
}
