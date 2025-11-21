namespace Mudosoft.Shared.Enums;

public enum CommandType
{
    Unknown = 0,
    Reboot = 1,
    Shutdown = 2,
    ExecuteScript = 3, // Yeni: Uzaktan Betik Çalıştırma
    // ... Diğer komut tipleri buraya eklenebilir
}