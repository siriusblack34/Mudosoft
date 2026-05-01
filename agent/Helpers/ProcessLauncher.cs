using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Orchestra.Agent.Native;

namespace Orchestra.Agent.Core;

public class ProcessLauncher
{
    private readonly ILogger<ProcessLauncher> _logger;
    private static readonly object _launchLock = new object();

    public ProcessLauncher(ILogger<ProcessLauncher> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Sadece hedef oturumda RDHelper'ı güvenli bir şekilde başlatır.
    /// Zaten çalışıyorsa veya başlatılamadıysa false döner.
    /// </summary>
    public bool LaunchInUserSession(uint sessionId, string executablePath, string args = "")
    {
        lock (_launchLock)
        {
            if (IsProcessRunningInSession(sessionId, executablePath))
            {
                _logger.LogInformation("[Session {SessionId}] RDHelper is already running in this session.", sessionId);
                return false;
            }

            IntPtr hToken = IntPtr.Zero;
            IntPtr hPrimaryToken = IntPtr.Zero;
            IntPtr lpEnvironment = IntPtr.Zero;
            var pi = new Win32Native.PROCESS_INFORMATION();

            try
            {
                // 1. Get the User Token for the target Session
                if (!Win32Native.WTSQueryUserToken(sessionId, out hToken))
                {
                    int error = Marshal.GetLastWin32Error();
                    _logger.LogWarning("[Session {SessionId}] WTSQueryUserToken failed. Error Code: {Error}. Session might not be logged in.", sessionId, error);
                    return false;
                }

                var sa = new Win32Native.SECURITY_ATTRIBUTES();
                sa.nLength = Marshal.SizeOf(sa);

                // 2. Duplicate the Token to Primary Token for CreateProcessAsUser
                if (!Win32Native.DuplicateTokenEx(
                    hToken,
                    Win32Native.MAXIMUM_ALLOWED,
                    ref sa,
                    Win32Native.SECURITY_IMPERSONATION_LEVEL.SecurityIdentification,
                    Win32Native.TOKEN_TYPE.TokenPrimary,
                    out hPrimaryToken))
                {
                    _logger.LogError("[Session {SessionId}] DuplicateTokenEx failed. Error: {Error}", sessionId, Marshal.GetLastWin32Error());
                    return false;
                }

                // 3. Create Environment variables specific to the user (%APPDATA%, %TEMP%, etc)
                if (!Win32Native.CreateEnvironmentBlock(out lpEnvironment, hPrimaryToken, false))
                {
                    _logger.LogError("[Session {SessionId}] CreateEnvironmentBlock failed. Error: {Error}", sessionId, Marshal.GetLastWin32Error());
                    return false;
                }

                // 4. Setup Target Desktop & Window Info
                var si = new Win32Native.STARTUPINFO();
                si.cb = Marshal.SizeOf(si);
                si.lpDesktop = @"winsta0\default"; // CRITICAL: Execute in interactive desktop
                si.dwFlags = 0x00000001; // STARTF_USESHOWWINDOW
                si.wShowWindow = 0;      // SW_HIDE — pencere görünmez başlar

                uint dwCreationFlags = Win32Native.NORMAL_PRIORITY_CLASS | Win32Native.CREATE_UNICODE_ENVIRONMENT | 0x08000000; // CREATE_NO_WINDOW
                string workingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty;

                // Create combined command line with quotes around executable path
                string commandLine = $"\"{executablePath}\" {args}".Trim();

                // 5. Create the Process
                bool result = Win32Native.CreateProcessAsUser(
                    hPrimaryToken,
                    executablePath, // lpApplicationName
                    commandLine,    // lpCommandLine
                    ref sa,
                    ref sa,
                    false,
                    dwCreationFlags,
                    lpEnvironment,
                    workingDirectory,
                    ref si,
                    out pi);

                if (!result)
                {
                    _logger.LogError("[Session {SessionId}] CreateProcessAsUser failed. Error: {Error}", sessionId, Marshal.GetLastWin32Error());
                    return false;
                }

                _logger.LogInformation("[Session {SessionId}] RDHelper started successfully. PID: {PID}", sessionId, pi.dwProcessId);
                return true;
            }
            finally
            {
                // 6. Cleanup Native Handles
                if (pi.hProcess != IntPtr.Zero) Win32Native.CloseHandle(pi.hProcess);
                if (pi.hThread != IntPtr.Zero) Win32Native.CloseHandle(pi.hThread);
                if (lpEnvironment != IntPtr.Zero) Win32Native.DestroyEnvironmentBlock(lpEnvironment);
                if (hPrimaryToken != IntPtr.Zero) Win32Native.CloseHandle(hPrimaryToken);
                if (hToken != IntPtr.Zero) Win32Native.CloseHandle(hToken);
            }
        }
    }

    private bool IsProcessRunningInSession(uint sessionId, string executablePath)
    {
        string processName = Path.GetFileNameWithoutExtension(executablePath);
        Process[] processes = Process.GetProcessesByName(processName);

        foreach (var p in processes)
        {
            try
            {
                if ((uint)p.SessionId == sessionId)
                    return true;
            }
            catch
            {
                // Ignore access denied on other processes
            }
        }
        return false;
    }

    public void KillProcessesInAllSessions(string executablePath)
    {
        string processName = Path.GetFileNameWithoutExtension(executablePath);
        Process[] processes = Process.GetProcessesByName(processName);

        foreach (var p in processes)
        {
            try
            {
                _logger.LogInformation("Killing active RDHelper process PID: {PID} in Session {SessionId}", p.Id, p.SessionId);
                p.Kill(true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to kill process {PID}", p.Id);
            }
        }
    }
}
