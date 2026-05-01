using System;
using System.Runtime.InteropServices;
using System.Security;

namespace Orchestra.Agent.Helpers;

public static class SessionInterop
{
    // ========== CONSTANTS ==========
    public const int TOKEN_DUPLICATE = 0x0002;
    public const int MAXIMUM_ALLOWED = 0x2000000;
    public const int CREATE_NEW_CONSOLE = 0x00000010;
    public const int NORMAL_PRIORITY_CLASS = 0x00000020;
    public const int CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    public const uint TOKEN_QUERY = 0x0008;
    public const uint TOKEN_ASSIGN_PRIMARY = 0x0001;
    public const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    public const uint SE_PRIVILEGE_ENABLED = 0x00000002;

    // ========== STRUCTURES ==========
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
    public struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LUID_AND_ATTRIBUTES
    {
        public LUID Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public LUID_AND_ATTRIBUTES Privileges;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WTS_SESSION_INFO
    {
        public uint SessionId;
        [MarshalAs(UnmanagedType.LPStr)]
        public string pWinStationName;
        public WTS_CONNECTSTATE_CLASS State;
    }

    public enum WTS_CONNECTSTATE_CLASS
    {
        WTSActive,
        WTSConnected,
        WTSConnectQuery,
        WTSShadow,
        WTSDisconnected,
        WTSIdle,
        WTSListen,
        WTSReset,
        WTSDown,
        WTSInit
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

    // ========== P/INVOKE ==========
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hSnapshot);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetCurrentProcess();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    public static extern bool WTSQueryUserToken(uint sessionId, out IntPtr phToken);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    public static extern bool WTSEnumerateSessions(
        IntPtr hServer,
        int Reserved,
        int Version,
        out IntPtr ppSessionInfo,
        out int pCount);

    [DllImport("wtsapi32.dll")]
    public static extern void WTSFreeMemory(IntPtr pMemory);

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

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges, ref TOKEN_PRIVILEGES NewState, uint BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

    // ========== LOGGING ==========
    private static void Log(string msg)
    {
        try
        {
            System.IO.File.AppendAllText(@"C:\mudosoft_session.log", $"{DateTime.Now}: {msg}{Environment.NewLine}");
        }
        catch { }
    }

    // ========== SE_TCB_NAME PRIVILEGE ==========
    /// <summary>
    /// SE_TCB_NAME privilege'ini etkinleştirir. WTSQueryUserToken bu privilege'i gerektirir.
    /// SYSTEM hesabı bu privilege'e sahiptir ama varsayılan olarak etkin olmayabilir.
    /// </summary>
    private static bool EnableSeTcbPrivilege()
    {
        try
        {
            IntPtr hToken;
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out hToken))
            {
                Log($"[PRIV] OpenProcessToken failed: {Marshal.GetLastWin32Error()}");
                return false;
            }

            LUID luid;
            if (!LookupPrivilegeValue(null, "SeTcbPrivilege", out luid))
            {
                Log($"[PRIV] LookupPrivilegeValue failed: {Marshal.GetLastWin32Error()}");
                CloseHandle(hToken);
                return false;
            }

            var tp = new TOKEN_PRIVILEGES
            {
                PrivilegeCount = 1,
                Privileges = new LUID_AND_ATTRIBUTES
                {
                    Luid = luid,
                    Attributes = SE_PRIVILEGE_ENABLED
                }
            };

            if (!AdjustTokenPrivileges(hToken, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero))
            {
                Log($"[PRIV] AdjustTokenPrivileges failed: {Marshal.GetLastWin32Error()}");
                CloseHandle(hToken);
                return false;
            }

            int lastErr = Marshal.GetLastWin32Error();
            CloseHandle(hToken);

            if (lastErr == 1300) // ERROR_NOT_ALL_ASSIGNED
            {
                Log("[PRIV] SeTcbPrivilege not assigned to this account");
                return false;
            }

            Log("[PRIV] SeTcbPrivilege enabled successfully");
            return true;
        }
        catch (Exception ex)
        {
            Log($"[PRIV] Exception: {ex.Message}");
            return false;
        }
    }

    // ========== SESSION ENUMERATION ==========
    /// <summary>
    /// Tüm aktif oturumları tara ve öncelik sırasına göre aday oturum listesi döndür.
    /// Öncelik: preferred session → console session → diğer Active → diğer Connected
    /// </summary>
    private static List<(uint id, string state)> GetCandidateSessions(uint preferredSessionId = 0xFFFFFFFF)
    {
        var candidates = new List<(uint id, string state)>();
        IntPtr pSessionInfo = IntPtr.Zero;
        int sessionCount = 0;

        try
        {
            uint consoleSession = WTSGetActiveConsoleSessionId();
            Log($"[SESSION] Console session: {consoleSession}");

            if (!WTSEnumerateSessions(IntPtr.Zero, 0, 1, out pSessionInfo, out sessionCount))
            {
                Log($"[SESSION] WTSEnumerateSessions failed: {Marshal.GetLastWin32Error()}");
                candidates.Add((consoleSession, "unknown"));
                return candidates;
            }

            int structSize = Marshal.SizeOf(typeof(WTS_SESSION_INFO));
            Log($"[SESSION] Found {sessionCount} sessions:");

            var activeSessions = new List<(uint id, string state)>();
            var connectedSessions = new List<(uint id, string state)>();

            for (int i = 0; i < sessionCount; i++)
            {
                IntPtr current = pSessionInfo + (i * structSize);
                var sessionInfo = Marshal.PtrToStructure<WTS_SESSION_INFO>(current);
                Log($"[SESSION]   Session {sessionInfo.SessionId}: {sessionInfo.pWinStationName} - {sessionInfo.State}");

                if (sessionInfo.SessionId == 0) continue;

                if (sessionInfo.State == WTS_CONNECTSTATE_CLASS.WTSActive)
                    activeSessions.Add((sessionInfo.SessionId, "WTSActive"));
                else if (sessionInfo.State == WTS_CONNECTSTATE_CLASS.WTSConnected)
                    connectedSessions.Add((sessionInfo.SessionId, "WTSConnected"));
            }

            // Build priority list: preferred → console → other Active → other Connected
            var allSessions = activeSessions.Concat(connectedSessions).ToList();

            // 1. Preferred session first (if valid)
            if (preferredSessionId != 0xFFFFFFFF)
            {
                var pref = allSessions.FirstOrDefault(s => s.id == preferredSessionId);
                if (pref.id == preferredSessionId)
                    candidates.Add(pref);
            }

            // 2. Console session (if not already added)
            var consoleSess = allSessions.FirstOrDefault(s => s.id == consoleSession);
            if (consoleSess.id == consoleSession && !candidates.Any(c => c.id == consoleSession))
                candidates.Add(consoleSess);

            // 3. All Active sessions (not already added)
            foreach (var s in activeSessions)
                if (!candidates.Any(c => c.id == s.id))
                    candidates.Add(s);

            // 4. All Connected sessions (not already added)
            foreach (var s in connectedSessions)
                if (!candidates.Any(c => c.id == s.id))
                    candidates.Add(s);

            if (candidates.Count == 0)
            {
                Log($"[SESSION] No candidate sessions found, using console: {consoleSession}");
                candidates.Add((consoleSession, "unknown"));
            }

            Log($"[SESSION] Candidate sessions (priority order): {string.Join(", ", candidates.Select(c => $"{c.id}({c.state})"))}");
            return candidates;
        }
        catch (Exception ex)
        {
            Log($"[SESSION] Exception: {ex.Message}");
            candidates.Add((WTSGetActiveConsoleSessionId(), "unknown"));
            return candidates;
        }
        finally
        {
            if (pSessionInfo != IntPtr.Zero) WTSFreeMemory(pSessionInfo);
        }
    }

    private static IntPtr TryGetUserToken(uint sessionId)
    {
        IntPtr hToken;
        if (WTSQueryUserToken(sessionId, out hToken))
        {
            Log($"[TOKEN] Got USER token via WTSQueryUserToken for session {sessionId}: {hToken}");
            return hToken;
        }

        int wtsErr = Marshal.GetLastWin32Error();
        Log($"[TOKEN] WTSQueryUserToken failed for session {sessionId}: error {wtsErr}");

        // Fallback 1: explorer.exe process'inden kullanıcı token'ı al
        hToken = GetUserTokenFromExplorer(sessionId);
        if (hToken != IntPtr.Zero)
        {
            Log($"[TOKEN] Got USER token from explorer.exe for session {sessionId}: {hToken}");
            return hToken;
        }

        // Fallback 2: winlogon.exe - login screen'de kullanıcı yokken bile çalışır
        hToken = GetTokenFromWinlogon(sessionId);
        if (hToken != IntPtr.Zero)
        {
            Log($"[TOKEN] Got SYSTEM token from winlogon.exe for session {sessionId}: {hToken}");
            return hToken;
        }

        Log($"[TOKEN] No token available for session {sessionId}");
        return IntPtr.Zero;
    }

    [DllImport("wtsapi32.dll", SetLastError = true)]
    public static extern bool WTSQuerySessionInformation(
        IntPtr hServer,
        uint sessionId,
        WTS_INFO_CLASS wtsInfoClass,
        out IntPtr ppBuffer,
        out uint pBytesReturned);

    public enum WTS_INFO_CLASS
    {
        WTSInitialProgram,
        WTSApplicationName,
        WTSWorkingDirectory,
        WTSOEMId,
        WTSSessionId,
        WTSUserName,
        WTSWinStationName,
        WTSDomainName,
        WTSConnectState,
        WTSClientBuildNumber,
        WTSClientName,
        WTSClientDirectory,
        WTSClientProductId,
        WTSClientHardwareId,
        WTSClientAddress,
        WTSClientDisplay,
        WTSClientProtocolType,
        WTSIdleTime,
        WTSLogonTime,
        WTSIncomingBytes,
        WTSOutgoingBytes,
        WTSIncomingFrames,
        WTSOutgoingFrames,
        WTSClientInfo,
        WTSSessionInfo,
        WTSSessionInfoEx,
        WTSConfigInfo,
        WTSValidationInfo,
        WTSSessionAddressV4,
        WTSIsRemoteSession
    }

    public static string? GetUsernameForSession(uint sessionId)
    {
        try
        {
            Log($"[SESSION INFO] Fetching user for session {sessionId} via Registry (LogonUI\\SessionData)...");
            
            string keyPath = $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\LogonUI\SessionData\{sessionId}";
            
            using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(keyPath))
            {
                if (key != null)
                {
                    string? samUser = key.GetValue("LoggedOnSAMUser") as string;
                    if (!string.IsNullOrEmpty(samUser))
                    {
                        Log($"[SESSION INFO] 🎯 Found SAM User from Registry: {samUser}");
                        return samUser;
                    }

                    string? user = key.GetValue("LoggedOnUser") as string;
                    if (!string.IsNullOrEmpty(user))
                    {
                        Log($"[SESSION INFO] 🎯 Found User from Registry: {user}");
                        return user;
                    }
                }
            }

            Log($"[SESSION INFO] Registry key {keyPath} or values not found.");
        }
        catch (Exception ex)
        {
            Log($"[SESSION INFO] Failed to read Registry for session {sessionId}: {ex.Message}");
        }

        return null;
    }

    // ========== ANA METOD ==========
    public static void CreateProcessInConsoleSession(string commandLine, bool waitForExit = false, uint targetSessionId = 0xFFFFFFFF)
    {
        IntPtr hToken = IntPtr.Zero;
        IntPtr hDupToken = IntPtr.Zero;
        IntPtr pEnv = IntPtr.Zero;

        try
        {
            // 1. SE_TCB_NAME privilege'ini etkinleştir (WTSQueryUserToken için gerekli)
            EnableSeTcbPrivilege();

            // 2. Aday oturumları öncelik sırasıyla al
            var candidates = GetCandidateSessions(targetSessionId);

            // 3. Her aday session'ı dene — token alınabilen ilkini kullan
            uint sessionId = 0xFFFFFFFF;
            string sessionState = "unknown";

            foreach (var (candidateId, candidateState) in candidates)
            {
                hToken = TryGetUserToken(candidateId);
                if (hToken != IntPtr.Zero)
                {
                    sessionId = candidateId;
                    sessionState = candidateState;
                    Log($"[SESSION] ✅ Using session {sessionId} (state: {sessionState}) — token acquired");
                    break;
                }
            }

            if (sessionId == 0xFFFFFFFF || hToken == IntPtr.Zero)
            {
                Log("[TOKEN] All candidate sessions failed — no user token available!");
                return;
            }

            // 4. Token'ı duplicate et (primary token olarak)
            if (!DuplicateTokenEx(hToken, MAXIMUM_ALLOWED, IntPtr.Zero,
                (int)SECURITY_IMPERSONATION_LEVEL.SecurityIdentification,
                (int)TOKEN_TYPE.TokenPrimary, out hDupToken))
            {
                int err = Marshal.GetLastWin32Error();
                Log($"[TOKEN] DuplicateTokenEx failed: {err}");
                return;
            }
            Log($"[TOKEN] Duplicated token: {hDupToken}");

            // 5. Kullanıcı environment block'unu oluştur
            if (!CreateEnvironmentBlock(out pEnv, hDupToken, false))
            {
                Log("[ENV] CreateEnvironmentBlock failed, continuing without environment");
                pEnv = IntPtr.Zero;
            }

            // 6. STARTUPINFO hazırla
            var si = new STARTUPINFO();
            si.cb = Marshal.SizeOf(si);
            si.lpDesktop = @"winsta0\default";
            si.dwFlags = 0x00000001; // STARTF_USESHOWWINDOW
            si.wShowWindow = 0; // SW_HIDE

            // 7. Exe path ve working directory'yi çıkar
            string exePath;
            if (commandLine.StartsWith('"'))
            {
                int endQuote = commandLine.IndexOf('"', 1);
                exePath = commandLine.Substring(1, endQuote - 1);
            }
            else
            {
                exePath = commandLine.Split(' ')[0];
            }

            string? workDir = System.IO.Path.GetDirectoryName(exePath);

            // 8. Process'i kullanıcı oturumunda başlat
            int creationFlags = CREATE_UNICODE_ENVIRONMENT | CREATE_NEW_CONSOLE | NORMAL_PRIORITY_CLASS;
            var pi = new PROCESS_INFORMATION();

            Log($"[LAUNCH] Creating process as USER");
            Log($"[LAUNCH] Command: {commandLine}");
            Log($"[LAUNCH] WorkDir: {workDir}");

            bool result = CreateProcessAsUser(
                hDupToken,
                exePath,
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
                Log($"[LAUNCH] CreateProcessAsUser failed: {err}");
                return;
            }

            Log($"[LAUNCH] ✅ Process created! PID: {pi.dwProcessId}");
            Log($"[SESSION] Launching helper in session {sessionId} (state {sessionState})");

            CloseHandle(pi.hProcess);
            CloseHandle(pi.hThread);
        }
        catch (Exception ex)
        {
            Log($"[LAUNCH] Exception: {ex}");
            throw;
        }
        finally
        {
            if (pEnv != IntPtr.Zero) DestroyEnvironmentBlock(pEnv);
            if (hDupToken != IntPtr.Zero) CloseHandle(hDupToken);
            if (hToken != IntPtr.Zero) CloseHandle(hToken);
        }
    }

    /// <summary>
    /// explorer.exe process'inden kullanıcı token'ı alır.
    /// explorer.exe her kullanıcı oturumunda KULLANICI olarak çalışır.
    /// winlogon.exe SYSTEM olarak çalışır ve .NET app crash ediyordu.
    /// </summary>
    private static IntPtr GetUserTokenFromExplorer(uint targetSessionId)
    {
        try
        {
            var explorerProcesses = System.Diagnostics.Process.GetProcessesByName("explorer");
            Log($"[EXPLORER] Found {explorerProcesses.Length} explorer processes");

            foreach (var proc in explorerProcesses)
            {
                try
                {
                    if (proc.SessionId == targetSessionId)
                    {
                        Log($"[EXPLORER] Found explorer in session {targetSessionId}, PID: {proc.Id}");

                        IntPtr hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, proc.Id);
                        if (hProcess == IntPtr.Zero)
                        {
                            Log($"[EXPLORER] OpenProcess failed: {Marshal.GetLastWin32Error()}");
                            continue;
                        }

                        IntPtr hToken;
                        if (!OpenProcessToken(hProcess, TOKEN_QUERY | TOKEN_DUPLICATE | TOKEN_ASSIGN_PRIMARY, out hToken))
                        {
                            Log($"[EXPLORER] OpenProcessToken failed: {Marshal.GetLastWin32Error()}");
                            CloseHandle(hProcess);
                            continue;
                        }

                        CloseHandle(hProcess);
                        Log($"[EXPLORER] Got user token: {hToken}");
                        return hToken;
                    }
                }
                catch (Exception ex)
                {
                    Log($"[EXPLORER] Error checking PID {proc.Id}: {ex.Message}");
                }
            }

            Log("[EXPLORER] No explorer found in target session");
        }
        catch (Exception ex)
        {
            Log($"[EXPLORER] Exception: {ex.Message}");
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// winlogon.exe process'inden token alır.
    /// Login screen'de kullanıcı yokken bile her session'da winlogon.exe SYSTEM olarak çalışır.
    /// Bu token ile helper'ı console session'a launch edebiliriz.
    /// </summary>
    private static IntPtr GetTokenFromWinlogon(uint targetSessionId)
    {
        try
        {
            var winlogonProcesses = System.Diagnostics.Process.GetProcessesByName("winlogon");
            Log($"[WINLOGON] Found {winlogonProcesses.Length} winlogon processes");

            foreach (var proc in winlogonProcesses)
            {
                try
                {
                    if (proc.SessionId == targetSessionId)
                    {
                        Log($"[WINLOGON] Found winlogon in session {targetSessionId}, PID: {proc.Id}");

                        IntPtr hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, proc.Id);
                        if (hProcess == IntPtr.Zero)
                        {
                            Log($"[WINLOGON] OpenProcess failed: {Marshal.GetLastWin32Error()}");
                            continue;
                        }

                        IntPtr hToken;
                        if (!OpenProcessToken(hProcess, TOKEN_QUERY | TOKEN_DUPLICATE | TOKEN_ASSIGN_PRIMARY, out hToken))
                        {
                            Log($"[WINLOGON] OpenProcessToken failed: {Marshal.GetLastWin32Error()}");
                            CloseHandle(hProcess);
                            continue;
                        }

                        CloseHandle(hProcess);
                        Log($"[WINLOGON] Got SYSTEM token: {hToken}");
                        return hToken;
                    }
                }
                catch (Exception ex)
                {
                    Log($"[WINLOGON] Error checking PID {proc.Id}: {ex.Message}");
                }
            }

            Log("[WINLOGON] No winlogon found in target session");
        }
        catch (Exception ex)
        {
            Log($"[WINLOGON] Exception: {ex.Message}");
        }

        return IntPtr.Zero;
    }
}
