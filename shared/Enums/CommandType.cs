using System.Text.Json.Serialization;

namespace Orchestra.Shared.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CommandType
{
    Unknown = 0,
    Reboot = 1,
    Shutdown = 2,
    ExecuteScript = 3,
    ListServices = 4,

    // File Operations
    FileList = 10,
    FileRead = 11,
    FileWrite = 12,
    FileDelete = 13,
    FolderCreate = 14,
    FolderCleanup = 15,  // Klasör içeriğini temizle (klasörü silme)

    // Agent Management
    UpdateAgent = 20,
    InstallVnc = 21
}
