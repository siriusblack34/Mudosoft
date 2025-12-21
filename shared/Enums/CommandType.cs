namespace Mudosoft.Shared.Enums;

public enum CommandType
{
    Unknown = 0,
    Reboot = 1,
    Shutdown = 2,
    ExecuteScript = 3,
    
    // File Operations
    FileList = 10,
    FileRead = 11,
    FileWrite = 12,
    FileDelete = 13,
    FolderCreate = 14,
    
    // Agent Management
    UpdateAgent = 20
}