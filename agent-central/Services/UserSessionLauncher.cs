using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Orchestra.CentralAgent.Services;

/// <summary>
/// Servis (Session 0) bağlamından kullanıcı oturumuna (Session 1+) process başlatır.
/// WTSQueryUserToken + CreateProcessAsUser yöntemi kullanır.
/// </summary>
[SupportedOSPlatform("windows")]
public static class UserSessionLauncher
{
    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr phToken);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessAsUser(
        IntPtr hToken,
        string? lpApplicationName,
        string lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool CreateEnvironmentBlock(out IntPtr lpEnvironment, IntPtr hToken, bool bInherit);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize;
        public int dwXCountChars, dwYCountChars;
        public int dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    private const uint NORMAL_PRIORITY_CLASS    = 0x00000020;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;

    /// <summary>
    /// Aktif kullanıcı oturumunda process başlatır. Başarılı olursa true döner.
    /// </summary>
    public static bool LaunchInUserSession(string exePath, string arguments)
    {
        var sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId == 0xFFFFFFFF) return false;

        if (!WTSQueryUserToken(sessionId, out var userToken)) return false;

        try
        {
            CreateEnvironmentBlock(out var envBlock, userToken, false);
            try
            {
                var si = new STARTUPINFO
                {
                    cb = Marshal.SizeOf<STARTUPINFO>(),
                    lpDesktop = "winsta0\\default"
                };

                bool ok = CreateProcessAsUser(
                    userToken,
                    null,
                    $"\"{exePath}\" {arguments}",
                    IntPtr.Zero, IntPtr.Zero,
                    false,
                    NORMAL_PRIORITY_CLASS | CREATE_UNICODE_ENVIRONMENT,
                    envBlock,
                    null,
                    ref si,
                    out var pi);

                if (ok)
                {
                    CloseHandle(pi.hProcess);
                    CloseHandle(pi.hThread);
                }

                return ok;
            }
            finally
            {
                if (envBlock != IntPtr.Zero) DestroyEnvironmentBlock(envBlock);
            }
        }
        finally
        {
            CloseHandle(userToken);
        }
    }
}
