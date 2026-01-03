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
        
        IntPtr hToken = IntPtr.Zero;
        IntPtr hDupToken = IntPtr.Zero;
        IntPtr pEnv = IntPtr.Zero;
        
        try
        {
            // 1. Aktif konsol session'ını bul
            uint sessionId = WTSGetActiveConsoleSessionId();
            Log($"Active console session: {sessionId}");
            
            if (sessionId == 0xFFFFFFFF)
            {
                Log("No active console session found!");
                return;
            }
            
            // 2. Session'ın kullanıcı token'ını al
            if (!WTSQueryUserToken(sessionId, out hToken))
            {
                int err = Marshal.GetLastWin32Error();
                Log($"WTSQueryUserToken failed: {err}");
                
                // Fallback: schtasks yöntemi
                LaunchViaSchtasks(commandLine);
                return;
            }
            Log($"Got user token: {hToken}");
            
            // 3. Token'ı duplicate et (primary token olarak)
            if (!DuplicateTokenEx(hToken, MAXIMUM_ALLOWED, IntPtr.Zero,
                (int)SECURITY_IMPERSONATION_LEVEL.SecurityIdentification,
                (int)TOKEN_TYPE.TokenPrimary, out hDupToken))
            {
                int err = Marshal.GetLastWin32Error();
                Log($"DuplicateTokenEx failed: {err}");
                CloseHandle(hToken);
                return;
            }
            Log($"Duplicated token: {hDupToken}");
            
            // 4. Kullanıcı environment block'unu oluştur
            if (!CreateEnvironmentBlock(out pEnv, hDupToken, false))
            {
                Log("CreateEnvironmentBlock failed, continuing without environment");
                pEnv = IntPtr.Zero;
            }
            
            // 5. STARTUPINFO hazırla
            var si = new STARTUPINFO();
            si.cb = Marshal.SizeOf(si);
            si.lpDesktop = @"winsta0\default"; // Default desktop
            si.dwFlags = 0x00000001; // STARTF_USESHOWWINDOW
            si.wShowWindow = 0; // SW_HIDE - hidden window
            
            // 6. Process'i kullanıcı oturumunda başlat
            var pi = new PROCESS_INFORMATION();
            
            // CREATE_UNICODE_ENVIRONMENT | CREATE_NEW_CONSOLE | NORMAL_PRIORITY_CLASS
            int creationFlags = 0x00000400 | 0x00000010 | 0x00000020;
            
            string? workDir = System.IO.Path.GetDirectoryName(commandLine.Trim('"').Split('"')[0]);
            
            Log($"Creating process: {commandLine}");
            Log($"Working dir: {workDir}");
            
            bool result = CreateProcessAsUser(
                hDupToken,
                null,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                creationFlags,
                pEnv,
                workDir,
                ref si,
                out pi
            );
            
            if (!result)
            {
                int err = Marshal.GetLastWin32Error();
                Log($"CreateProcessAsUser failed: {err}");
                
                // Fallback: schtasks
                LaunchViaSchtasks(commandLine);
                return;
            }
            
            Log($"Process created! PID: {pi.dwProcessId}");
            
            // Handle'ları kapat
            CloseHandle(pi.hProcess);
            CloseHandle(pi.hThread);
        }
        catch (Exception ex)
        {
            Log($"CreateProcessInConsoleSession exception: {ex}");
            throw;
        }
        finally
        {
            if (pEnv != IntPtr.Zero) DestroyEnvironmentBlock(pEnv);
            if (hDupToken != IntPtr.Zero) CloseHandle(hDupToken);
            if (hToken != IntPtr.Zero) CloseHandle(hToken);
        }
    }
    
    private static void LaunchViaSchtasks(string commandLine)
    {
        void Log(string msg) => System.IO.File.AppendAllText(@"C:\mudosoft_session.log", $"{DateTime.Now}: [SCHTASKS] {msg}{Environment.NewLine}");
        
        string taskName = $"MudosoftHelper_{Guid.NewGuid():N}";
        
        // Extract exe path from command line
        string exePath;
        if (commandLine.StartsWith("\""))
        {
            var endQuote = commandLine.IndexOf('"', 1);
            exePath = commandLine.Substring(1, endQuote - 1);
        }
        else
        {
            exePath = commandLine.Split(' ')[0];
        }
        
        Log($"Creating scheduled task: {taskName}");
        Log($"Exe path: {exePath}");
        
        try
        {
            // Create task
            var createProc = new System.Diagnostics.Process();
            createProc.StartInfo.FileName = "schtasks.exe";
            createProc.StartInfo.Arguments = $"/Create /TN \"{taskName}\" /TR \"\\\"{exePath}\\\" --desktop-helper\" /SC ONCE /ST 00:00 /F /RL HIGHEST";
            createProc.StartInfo.UseShellExecute = false;
            createProc.StartInfo.CreateNoWindow = true;
            createProc.StartInfo.RedirectStandardOutput = true;
            createProc.StartInfo.RedirectStandardError = true;
            createProc.Start();
            var createErr = createProc.StandardError.ReadToEnd();
            createProc.WaitForExit(5000);
            Log($"Task created, exit: {createProc.ExitCode}, err: {createErr}");
            
            // Run task
            var runProc = new System.Diagnostics.Process();
            runProc.StartInfo.FileName = "schtasks.exe";
            runProc.StartInfo.Arguments = $"/Run /TN \"{taskName}\"";
            runProc.StartInfo.UseShellExecute = false;
            runProc.StartInfo.CreateNoWindow = true;
            runProc.Start();
            runProc.WaitForExit(5000);
            Log($"Task run, exit: {runProc.ExitCode}");
            
            // Delete task async
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
            Log($"Schtasks error: {ex.Message}");
            throw;
        }
    }
}
