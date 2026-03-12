using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Mudosoft.Agent.Core;
using Mudosoft.Agent.Native;

namespace Mudosoft.Agent.Services;

/// <summary>
/// HelperLauncher - Enterprise Level Remote UI Bootstrapper
/// Uses supported WTS APIs to safely pierce Session 0 isolation
/// and securely duplicates interactive user tokens to launch RDHelper
/// inside the active session context.
/// </summary>
public class HelperLauncher : BackgroundService
{
    private readonly ILogger<HelperLauncher> _logger;
    private readonly ProcessLauncher _processLauncher;
    private readonly string _helperPath;
    private Thread? _wtsListenerThread;

    public HelperLauncher(ILogger<HelperLauncher> logger, ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _processLauncher = new ProcessLauncher(loggerFactory.CreateLogger<ProcessLauncher>());
        
        var agentDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppDomain.CurrentDomain.BaseDirectory;
        _helperPath = Path.GetFullPath(Path.Combine(agentDir, "MudoSoft.RDHelper.exe"));
        
        // Development fallback testing if running from VS
        if (!File.Exists(_helperPath))
        {
            string fallback = Path.GetFullPath(Path.Combine(agentDir, "..", "..", "..", "..", "helper", "MudoSoft.RDHelper", "bin", "Debug", "net8.0-windows", "MudoSoft.RDHelper.exe"));
            if (File.Exists(fallback))
            {
                _helperPath = fallback;
            }
        }
    }
    
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("HelperLauncher started. Target Helper Path: {HelperPath}", _helperPath);

        // 1. Initial Launch
        LaunchInActiveSession();

        // 2. Start WTS Listener Background Thread (Long Running, Unmanaged Block)
        _wtsListenerThread = new Thread(() => ListenToWtsEvents(stoppingToken))
        {
            IsBackground = true,
            Name = "WTS Session Listener Thread"
        };
        _wtsListenerThread.Start();

        return Task.CompletedTask;
    }

    private void LaunchInActiveSession()
    {
        if (!File.Exists(_helperPath))
        {
            _logger.LogWarning("Cannot launch helper, file not found: {Path}", _helperPath);
            return;
        }

        uint activeSessionId = Win32Native.WTSGetActiveConsoleSessionId();
        if (activeSessionId != 0xFFFFFFFF)
        {
            _logger.LogInformation("Attempting to launch RDHelper in Active Console Session ID: {SessionId}", activeSessionId);
            _processLauncher.LaunchInUserSession(activeSessionId, _helperPath, "--desktop-helper");
        }
    }

    private void ListenToWtsEvents(CancellationToken stoppingToken)
    {
        _logger.LogInformation("WTS Session Listener Thread running. Waiting for Session Logon/Connect changes...");
        
        while (!stoppingToken.IsCancellationRequested)
        {
            // Block indefinitely on system events. True hardware level hook.
            if (Win32Native.WTSWaitSystemEvent(IntPtr.Zero, Win32Native.WTS_EVENT_ALL, out uint eventFlags))
            {
                _logger.LogInformation("WTS Event Received. MASK: {EventFlags}", eventFlags);

                // We care about Session Connects and Logons to ensure UI spawns reliably
                if ((eventFlags & Win32Native.WTS_EVENT_LOGON) != 0 ||
                    (eventFlags & Win32Native.WTS_EVENT_CONNECT) != 0 ||
                    (eventFlags & Win32Native.WTS_EVENT_STATECHANGE) != 0)
                {
                    // Slight delay allows token initialization safely
                    Thread.Sleep(500);
                    LaunchInActiveSession();
                }
                
                if ((eventFlags & Win32Native.WTS_EVENT_LOGOFF) != 0 ||
                    (eventFlags & Win32Native.WTS_EVENT_DISCONNECT) != 0)
                {
                    _logger.LogInformation("Session Logoff/Disconnect detected.");
                }
            }
            else
            {
                int error = Marshal.GetLastWin32Error();
                _logger.LogWarning("WTSWaitSystemEvent block failed. Error: {Error}. Sleeping 5s before retry.", error);
                Thread.Sleep(5000);
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Service Stop requested. Killing active UI helpers spawned by this process.");
        // Gracefully kill RDHelpers
        _processLauncher.KillProcessesInAllSessions(_helperPath);
        
        // Unblock WTS listener wait thread if possible by sending null mask, but practically 
        // the background thread will terminate when the process dies anyway.
        await base.StopAsync(cancellationToken);
    }
}
