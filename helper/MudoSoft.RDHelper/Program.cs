using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;
using MudoSoft.RDHelper.Services;

namespace MudoSoft.RDHelper;

internal static class Program
{
    private static readonly string LogPath = @"C:\ProgramData\MudoSoft\rdhelper.log";
    private static readonly string ConfigPath = @"C:\Program Files\MudoSoft\Agent\appsettings.json";

    private static string _deviceId = "";
    private static string _backendUrl = "http://10.0.213.89:5102";
    private static Rectangle _screenBounds = new(0, 0, 1920, 1080);

    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var hiddenForm = new Form
        {
            ShowInTaskbar = false,
            WindowState = FormWindowState.Minimized,
            Opacity = 0,
            FormBorderStyle = FormBorderStyle.None
        };

        hiddenForm.Load += async (s, e) =>
        {
            await RunHelperAsync(args);
        };

        Application.Run(hiddenForm);
    }

    static async Task RunHelperAsync(string[] args)
    {
        Log("=== RDHelper Started (WebRTC VP8 Mode) ===");

        _screenBounds = Screen.PrimaryScreen?.Bounds
            ?? new Rectangle(0, 0, 1920, 1080);

        Log($"Screen: {_screenBounds.Width}x{_screenBounds.Height}");

        ReadConfig();

        if (args.Length > 0)
            _deviceId = args[0];
        else
            _deviceId = GetDeviceId();

        Log($"Device ID: {_deviceId}");
        Log($"Backend URL: {_backendUrl}");

        try
        {
            var webRtcService = new WebRTCService(_deviceId, _backendUrl, _screenBounds);
            webRtcService.OnLog += Log;

            await webRtcService.StartAsync();

            Log("✅ WebRTC Service running.");

            await Task.Delay(Timeout.Infinite);
        }
        catch (Exception ex)
        {
            Log($"Fatal error: {ex}");
        }
    }

    static void ReadConfig()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var config = JsonSerializer.Deserialize<JsonElement>(json);

                if (config.TryGetProperty("Agent", out var agent) &&
                    agent.TryGetProperty("BackendUrl", out var url))
                {
                    _backendUrl = url.GetString() ?? _backendUrl;
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Config read error: {ex.Message}");
        }
    }

    static string GetDeviceId()
    {
        var exeDir = AppContext.BaseDirectory;
        var idPath = Path.Combine(exeDir, "device_id.txt");

        if (File.Exists(idPath))
            return File.ReadAllText(idPath).Trim();

        var fallbackPath = @"C:\ProgramData\MudoSoft\device_id.txt";
        if (File.Exists(fallbackPath))
            return File.ReadAllText(fallbackPath).Trim();

        return Guid.NewGuid().ToString("N");
    }

    static void Log(string message)
    {
        var logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";

        try
        {
            var logDir = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
                Directory.CreateDirectory(logDir);

            File.AppendAllText(LogPath, logLine + Environment.NewLine);
        }
        catch { }
    }
}