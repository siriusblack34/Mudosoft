using System;
using System.Runtime.InteropServices;
using System.Security;

namespace Mudosoft.Agent.Helpers;

public static class SessionInterop
{
    public const int TOKEN_DUPLICATE = 0x0002;
    public const int MAXIMUM_ALLOWED = 0x2000000;
    public const int CREATE_NEW_CONSOLE = 0x00000010;

    public const int IDLE_PRIORITY_CLASS = 0x40;
    public const int NORMAL_PRIORITY_CLASS = 0x20;
    public const int HIGH_PRIORITY_CLASS = 0x80;
    public const int REALTIME_PRIORITY_CLASS = 0x100;

    public static readonly int PROCESS_QUERY_INFORMATION = 0x0400;
    public static readonly int PROCESS_VM_READ = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    public struct STARTUPINFO
    {
        public int cb;
        public String lpReserved;
        public String lpDesktop;
        public String lpTitle;
        public uint dwX;
        public uint dwY;
        public uint dwXSize;
        public uint dwYSize;
        public uint dwXCountChars;
        public uint dwYCountChars;
        public uint dwFillAttribute;
        public uint dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SECURITY_ATTRIBUTES
    {
        public int Length;
        public IntPtr lpSecurityDescriptor;
        public bool bInheritHandle;
    }

    public enum SECURITY_IMPERSONATION_LEVEL
    {
        SecurityAnonymous,
        SecurityIdentification,
        SecurityImpersonation,
        SecurityDelegation
    }

    public enum TOKEN_TYPE
    {
        TokenPrimary = 1,
        TokenImpersonation
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hSnapshot);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    public static extern bool WTSQueryUserToken(uint sessionId, out IntPtr phToken);

    [DllImport("advapi32.dll", EntryPoint = "DuplicateTokenEx")]
    public static extern bool DuplicateTokenEx(IntPtr hExistingToken, uint dwDesiredAccess, IntPtr lpThreadAttributes, int ImpersonationLevel, int TokenType, out IntPtr phNewToken);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern bool CreateProcessAsUser(
        IntPtr hToken,
        string? lpApplicationName,
        string? lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        int dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);
    
    [DllImport("userenv.dll", SetLastError = true)]
    public static extern bool CreateEnvironmentBlock(out IntPtr lpEnvironment, IntPtr hToken, bool bInherit);

    [DllImport("userenv.dll", SetLastError = true)]
    public static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

    public const uint TOKEN_QUERY = 0x0008;
    public const uint TOKEN_ASSIGN_PRIMARY = 0x0001;
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    public static void CreateProcessInConsoleSession(string commandLine, bool waitForExit = false)
    {
        void Log(string msg) => System.IO.File.AppendAllText(@"C:\mudosoft_session.log", $"{DateTime.Now}: {msg}{Environment.NewLine}");
        
        // schtasks.exe kullanarak kullanıcı oturumunda process başlat
        // Bu yöntem CreateProcessAsUser'dan daha güvenilir çalışır
        
        string taskName = $"MudosoftHelper_{Guid.NewGuid():N}";
        string exePath = commandLine.Trim('"').Split('"')[0]; // İlk quoted path'i al
        
        // Eğer commandLine quotes içindeyse, bütün halinde kullan
        if (commandLine.StartsWith("\""))
        {
            var parts = commandLine.Split(new[] { "\" " }, 2, StringSplitOptions.None);
            exePath = parts[0].Trim('"');
        }
        
        Log($"Creating scheduled task: {taskName}");
        Log($"Command: {commandLine}");
        
        try
        {
            // Doğrudan schtasks ile çalıştır (PowerShell wrapper kaldırıldı - quote sorunları yarattı)
            var createProc = new System.Diagnostics.Process();
            createProc.StartInfo.FileName = "schtasks.exe";
            createProc.StartInfo.Arguments = $"/Create /TN \"{taskName}\" /TR \"\\\"{commandLine.Split(' ')[0].Trim('\"')}\\\" --desktop-helper\" /SC ONCE /ST 00:00 /F /RL HIGHEST";
            createProc.StartInfo.UseShellExecute = false;
            createProc.StartInfo.CreateNoWindow = true;
            createProc.StartInfo.RedirectStandardOutput = true;
            createProc.StartInfo.RedirectStandardError = true;
            createProc.Start();
            createProc.WaitForExit(5000);
            Log($"Task created, exit code: {createProc.ExitCode}");
            
            // Task'ı hemen çalıştır
            var runProc = new System.Diagnostics.Process();
            runProc.StartInfo.FileName = "schtasks.exe";
            runProc.StartInfo.Arguments = $"/Run /TN \"{taskName}\"";
            runProc.StartInfo.UseShellExecute = false;
            runProc.StartInfo.CreateNoWindow = true;
            runProc.StartInfo.RedirectStandardOutput = true;
            runProc.StartInfo.RedirectStandardError = true;
            runProc.Start();
            runProc.WaitForExit(5000);
            Log($"Task run triggered, exit code: {runProc.ExitCode}");
            
            // Task'ı sil (temizlik)
            System.Threading.Tasks.Task.Run(async () =>
            {
                await System.Threading.Tasks.Task.Delay(5000);
                try
                {
                    var deleteProc = new System.Diagnostics.Process();
                    deleteProc.StartInfo.FileName = "schtasks.exe";
                    deleteProc.StartInfo.Arguments = $"/Delete /TN \"{taskName}\" /F";
                    deleteProc.StartInfo.UseShellExecute = false;
                    deleteProc.StartInfo.CreateNoWindow = true;
                    deleteProc.Start();
                }
                catch { }
            });
        }
        catch (Exception ex)
        {
            Log($"Scheduled task error: {ex.Message}");
            throw;
        }
    }
}
