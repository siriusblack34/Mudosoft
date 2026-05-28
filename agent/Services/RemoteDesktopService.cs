using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestra.Agent.Interfaces;
using Orchestra.Agent.Models;
using Orchestra.Agent.Helpers;
using Orchestra.Shared.Dtos;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Diagnostics;

namespace Orchestra.Agent.Services;

[SupportedOSPlatform("windows")]
public class RemoteDesktopService : BackgroundService
{
    private readonly ILogger<RemoteDesktopService> _logger;
    private readonly IDeviceIdentityProvider _identityProvider;
    private readonly AgentConfig _config;
    private HubConnection? _hubConnection;
    private bool _isStreaming = false;
    private readonly InputSimulator _inputSimulator;
    private readonly RemoteDesktopConfig _rdConfig;

    // Monitor selection: -1 = all monitors (virtual screen), 0+ = specific monitor
    private int _selectedMonitor = 0;

    // Streaming quality — controllable from frontend via hub message
    private int _jpegQuality = 75;
    private int _targetWidth = 1600;

    // ── Manager-mode state ─────────────────────────────────────────────
    // Exponential backoff for helper launch retries
    private int _helperRetryCount = 0;
    private DateTime _nextHelperAttempt = DateTime.MinValue;
    private static readonly TimeSpan[] _backoffIntervals =
    {
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(60),
        TimeSpan.FromSeconds(120),
        TimeSpan.FromSeconds(300)  // max 5 min
    };

    public RemoteDesktopService(
        ILogger<RemoteDesktopService> logger,
        IDeviceIdentityProvider identityProvider,
        IOptions<AgentConfig> config,
        RemoteDesktopConfig rdConfig)
    {
        _logger = logger;
        _identityProvider = identityProvider;
        _config = config.Value;
        _rdConfig = rdConfig;
        _inputSimulator = new InputSimulator();
    }

    private void LogDebug(string message)
    {
        try { File.AppendAllText(@"C:\Users\Public\mudosoft_manager_debug.log", $"{DateTime.Now}: {message}{Environment.NewLine}"); }
        catch { }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogDebug($"ExecuteAsync started. Mode: {_rdConfig.Mode}");
        if (_rdConfig.Mode == "Helper")
            await RunHelperMode(stoppingToken);
        else
            await RunManagerMode(stoppingToken);
    }

    // ── HELPER MODE (User Session) ────────────────────────────────────

    private async Task RunHelperMode(CancellationToken stoppingToken)
    {
        LogDebug("Helper Mode initializing...");
        UpdateHelperHeartbeat();

        try
        {
            var deviceId = _identityProvider.GetDeviceId();
            var hubUrl = $"{_config.BackendUrl}/hubs/desktop";
            LogDebug($"Connecting to hub: {hubUrl} deviceId: {deviceId}");

            _hubConnection = new HubConnectionBuilder()
                .WithUrl(hubUrl, options =>
                {
                    options.ApplicationMaxBufferSize = 5 * 1024 * 1024;
                })
                .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30) })
                .Build();

            _hubConnection.Reconnected += async (connectionId) =>
            {
                LogDebug("Helper reconnected — re-registering...");
                try
                {
                    await _hubConnection.InvokeAsync("RegisterDevice", deviceId);
                    LogDebug("Re-registered after reconnect");
                }
                catch (Exception ex) { LogDebug($"Re-register failed: {ex.Message}"); }
            };

            _hubConnection.On<InputEventDto>("PerformInput", input =>
            {
                try { _inputSimulator.HandleInput(input, GetScreenBounds()); }
                catch (Exception ex) { _logger.LogWarning("Input error: {Msg}", ex.Message); }
            });

            _hubConnection.On("StartStreaming", () =>
            {
                LogDebug("StartStreaming received");
                _isStreaming = true;
            });

            _hubConnection.On("StopStreaming", () =>
            {
                LogDebug("StopStreaming received");
                _isStreaming = false;
            });

            _hubConnection.On<int>("SelectMonitor", monitorIndex =>
            {
                _selectedMonitor = monitorIndex;
                LogDebug($"Monitor selected: {monitorIndex}");
            });

            // Quality control from frontend
            _hubConnection.On<int, int>("SetQuality", (quality, width) =>
            {
                _jpegQuality = Math.Clamp(quality, 20, 95);
                _targetWidth = Math.Clamp(width, 640, 1920);
                LogDebug($"Quality set: JPEG={_jpegQuality} Width={_targetWidth}");
            });

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (_hubConnection.State == HubConnectionState.Disconnected)
                    {
                        await _hubConnection.StartAsync(stoppingToken);
                        LogDebug("Hub connected!");
                        await _hubConnection.InvokeAsync("RegisterDevice", deviceId, stoppingToken);
                        _isStreaming = true;
                    }

                    if (_isStreaming && _hubConnection.State == HubConnectionState.Connected)
                    {
                        UpdateHelperHeartbeat();
                        await CaptureAndSendFrame(deviceId);
                        await Task.Delay(10, stoppingToken);
                    }
                    else
                    {
                        UpdateHelperHeartbeat();
                        await Task.Delay(1000, stoppingToken);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    LogDebug($"Helper loop error: {ex.Message}");
                    _isStreaming = false;
                    UpdateHelperHeartbeat();
                    await Task.Delay(5000, stoppingToken);
                }
            }
        }
        catch (Exception ex)
        {
            LogDebug($"Helper FATAL: {ex}");
            _logger.LogError(ex, "Helper fatal error");
        }
    }

    // ── MANAGER MODE (Service Session 0) ─────────────────────────────

    private async Task RunManagerMode(CancellationToken stoppingToken)
    {
        LogDebug("Manager mode started. Monitoring helper...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!IsHelperAlive())
                {
                    if (DateTime.Now < _nextHelperAttempt)
                    {
                        // Backoff — wait, don't spam
                        await Task.Delay(1000, stoppingToken);
                        continue;
                    }

                    uint sessionId;
                    try { sessionId = SessionInterop.WTSGetActiveConsoleSessionId(); }
                    catch (Exception ex)
                    {
                        LogDebug($"Session check error: {ex.Message}");
                        ScheduleNextAttempt();
                        await Task.Delay(5000, stoppingToken);
                        continue;
                    }

                    if (sessionId == 0xFFFFFFFF)
                    {
                        // No active user session yet — reset retry count, try again soon
                        _helperRetryCount = 0;
                        _nextHelperAttempt = DateTime.MinValue;
                        await Task.Delay(10000, stoppingToken);
                        continue;
                    }

                    LogDebug($"Active session: {sessionId}. Launching helper (attempt #{_helperRetryCount + 1})...");
                    bool launched = LaunchHelper(sessionId);
                    ScheduleNextAttempt();

                    if (launched)
                    {
                        // Wait a moment before checking if helper came alive
                        await Task.Delay(3000, stoppingToken);
                        if (IsHelperAlive())
                        {
                            LogDebug("Helper is alive!");
                            _helperRetryCount = 0;
                            _nextHelperAttempt = DateTime.MinValue;
                        }
                    }
                }
                else
                {
                    // Helper healthy — reset retry state
                    _helperRetryCount = 0;
                    _nextHelperAttempt = DateTime.MinValue;
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                LogDebug($"Manager loop error: {ex.Message}");
            }

            await Task.Delay(10000, stoppingToken);
        }
    }

    private void ScheduleNextAttempt()
    {
        var idx = Math.Min(_helperRetryCount, _backoffIntervals.Length - 1);
        var delay = _backoffIntervals[idx];
        _helperRetryCount++;
        _nextHelperAttempt = DateTime.Now.Add(delay);
        LogDebug($"Next helper launch attempt in {delay.TotalSeconds}s (retry #{_helperRetryCount})");
    }

    // ── Helper Lifecycle ──────────────────────────────────────────────

    private static readonly string HelperFlagPath = @"C:\Users\Public\MudoSoftHelper.flag";

    private bool IsHelperAlive()
    {
        try
        {
            if (!File.Exists(HelperFlagPath)) return false;
            var lastWrite = File.GetLastWriteTime(HelperFlagPath);
            var age = (DateTime.Now - lastWrite).TotalSeconds;
            if (age > 60)
            {
                LogDebug($"Helper flag stale ({age:0}s old) — helper crashed or stopped");
                return false;
            }
            return true;
        }
        catch { return false; }
    }

    private void UpdateHelperHeartbeat()
    {
        try { File.WriteAllText(HelperFlagPath, DateTime.Now.ToString("o")); }
        catch { }
    }

    private bool LaunchHelper(uint sessionId)
    {
        try
        {
            string exePath = Environment.ProcessPath!;
            string taskName = "MudoSoft_RDHelper";

            string? activeUsername = SessionInterop.GetUsernameForSession(sessionId);
            LogDebug($"Launching helper for user: {activeUsername ?? "UNKNOWN"}, session: {sessionId}");

            string userPrincipalNode = string.IsNullOrEmpty(activeUsername)
                ? "<LogonType>InteractiveToken</LogonType>"
                : $"<UserId>{activeUsername}</UserId>\n      <LogonType>InteractiveToken</LogonType>";

            string xmlPath = Path.Combine(Path.GetTempPath(), "rdhelper_task.xml");
            string xmlContent = $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>MudoSoft Remote Desktop Helper</Description>
  </RegistrationInfo>
  <Triggers />
  <Principals>
    <Principal id=""Author"">
      {userPrincipalNode}
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>Parallel</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>false</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>true</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>4</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{exePath}</Command>
      <Arguments>--desktop-helper</Arguments>
    </Exec>
  </Actions>
</Task>";
            File.WriteAllText(xmlPath, xmlContent, System.Text.Encoding.Unicode);

            // Register task (force overwrite)
            var (createOut, createErr) = RunProcess("schtasks.exe",
                $"/create /tn \"{taskName}\" /xml \"{xmlPath}\" /f");
            if (!string.IsNullOrEmpty(createErr))
                LogDebug($"schtasks create err: {createErr}");

            // Run task
            var (runOut, runErr) = RunProcess("schtasks.exe", $"/run /tn \"{taskName}\"");
            if (!string.IsNullOrEmpty(runErr))
                LogDebug($"schtasks run err: {runErr}");

            try { File.Delete(xmlPath); } catch { }

            LogDebug("Helper task scheduled and triggered");
            return true;
        }
        catch (Exception ex)
        {
            LogDebug($"LaunchHelper failed: {ex}");
            return false;
        }
    }

    private static (string stdout, string stderr) RunProcess(string exe, string args)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var proc = Process.Start(psi)!;
        proc.WaitForExit(5000);
        return (proc.StandardOutput.ReadToEnd(), proc.StandardError.ReadToEnd());
    }

    // ── Screen Capture ───────────────────────────────────────────────

    private async Task CaptureAndSendFrame(string deviceId)
    {
        try
        {
            if (!OperatingSystem.IsWindows()) return;

            var bounds = GetScreenBounds();
            using var bitmap = new Bitmap(bounds.Width, bounds.Height);
            using (var g = Graphics.FromImage(bitmap))
                g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);

            using var resized = ResizeImage(bitmap, _targetWidth);
            using var stream = new MemoryStream();
            SaveJpeg(resized, stream, _jpegQuality);
            var imageBytes = stream.ToArray();

            if (_hubConnection != null)
                await _hubConnection.InvokeAsync("StreamFrame", deviceId, imageBytes);
        }
        catch { /* suppress per-frame errors */ }
    }

    private delegate bool MonitorEnumDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumDelegate lpfnEnum, IntPtr dwData);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private List<Rectangle> _monitorBounds = new();
    private DateTime _lastMonitorRefresh = DateTime.MinValue;

    private void RefreshMonitorList()
    {
        if ((DateTime.Now - _lastMonitorRefresh).TotalSeconds < 5 && _monitorBounds.Count > 0)
            return;

        var newList = new List<Rectangle>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT r, IntPtr data) =>
            {
                newList.Add(new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top));
                return true;
            }, IntPtr.Zero);

        if (newList.Count > 0) _monitorBounds = newList;
        _lastMonitorRefresh = DateTime.Now;
    }

    private Rectangle GetScreenBounds()
    {
        if (_selectedMonitor >= 0)
        {
            RefreshMonitorList();
            if (_selectedMonitor < _monitorBounds.Count) return _monitorBounds[_selectedMonitor];
            if (_monitorBounds.Count > 0) return _monitorBounds[0];
        }
        return GetVirtualScreenBounds();
    }

    private Rectangle GetVirtualScreenBounds()
    {
        int vLeft = InputSimulator.GetSystemMetrics(InputSimulator.SM_XVIRTUALSCREEN);
        int vTop = InputSimulator.GetSystemMetrics(InputSimulator.SM_YVIRTUALSCREEN);
        int vWidth = InputSimulator.GetSystemMetrics(InputSimulator.SM_CXVIRTUALSCREEN);
        int vHeight = InputSimulator.GetSystemMetrics(InputSimulator.SM_CYVIRTUALSCREEN);
        if (vWidth == 0) vWidth = InputSimulator.GetSystemMetrics(0);
        if (vHeight == 0) vHeight = InputSimulator.GetSystemMetrics(1);
        return new Rectangle(vLeft, vTop, vWidth, vHeight);
    }

    public static int GetMonitorCount()
    {
        int count = 0;
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (IntPtr h, IntPtr hdc, ref RECT r, IntPtr d) => { count++; return true; }, IntPtr.Zero);
        return count;
    }

    private Bitmap ResizeImage(Bitmap image, int width)
    {
        int height = (int)((double)image.Height * width / image.Width);
        var dest = new Bitmap(width, height);
        dest.SetResolution(image.HorizontalResolution, image.VerticalResolution);
        using var g = Graphics.FromImage(dest);
        g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
        g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Low;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighSpeed;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighSpeed;
        using var wrapMode = new ImageAttributes();
        wrapMode.SetWrapMode(System.Drawing.Drawing2D.WrapMode.TileFlipXY);
        g.DrawImage(image, new Rectangle(0, 0, width, height), 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, wrapMode);
        return dest;
    }

    private void SaveJpeg(Bitmap image, MemoryStream stream, long quality)
    {
        var encoder = ImageCodecInfo.GetImageEncoders().FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid)
            ?? ImageCodecInfo.GetImageEncoders()[0];
        var encoderParams = new EncoderParameters(1);
        encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
        image.Save(stream, encoder, encoderParams);
    }
}
