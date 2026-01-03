using System.Drawing;
using System.Text.Json;
using MudoSoft.RDHelper.Services;

namespace MudoSoft.RDHelper;

/// <summary>
/// RDHelper - WebRTC Remote Desktop Helper
/// Uses SIPSorcery for P2P VP8 video streaming
/// </summary>
class Program
{
    private static readonly string LogPath = @"C:\ProgramData\MudoSoft\rdhelper.log";
    private static readonly string ConfigPath = @"C:\Program Files\MudoSoft\Agent\appsettings.json";
    
    private static string _deviceId = "";
    private static string _backendUrl = "http://localhost:5102";
    private static Rectangle _screenBounds = new(0, 0, 1920, 1080);
    
    static async Task Main(string[] args)
    {
        Log("=== RDHelper Started (WebRTC VP8 Mode) ===");
        
        // Get screen bounds
        _screenBounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds 
            ?? new Rectangle(0, 0, 1920, 1080);
        Log($"Screen: {_screenBounds.Width}x{_screenBounds.Height}");
        
        // Read config
        ReadConfig();
        
        // Get device ID
        if (args.Length > 0)
        {
            _deviceId = args[0];
        }
        else
        {
            _deviceId = GetDeviceId();
        }
        Log($"Device ID: {_deviceId}");
        Log($"Backend URL: {_backendUrl}");
        
        try
        {
            // Create and start WebRTC service
            var webRtcService = new WebRTCService(_deviceId, _backendUrl, _screenBounds);
            webRtcService.OnLog += Log;
            
            await webRtcService.StartAsync();
            
            // Keep running until exit signal
            Log("✅ WebRTC Service running. Press Ctrl+C to stop.");
            
            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };
            
            // Keep alive loop
            while (!cts.Token.IsCancellationRequested)
            {
                await Task.Delay(1000, cts.Token).ContinueWith(t => { });
            }
            
            webRtcService.Dispose();
        }
        catch (Exception ex)
        {
            Log($"Fatal error: {ex.Message}\n{ex.StackTrace}");
        }
        
        Log("=== RDHelper Stopped ===");
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
        
        Log($"Looking for device ID at: {idPath}");
        
        if (File.Exists(idPath))
        {
            var id = File.ReadAllText(idPath).Trim();
            Log($"Found device ID in file: {id}");
            return id;
        }
        
        var fallbackPath = @"C:\ProgramData\MudoSoft\device_id.txt";
        if (File.Exists(fallbackPath))
        {
            var id = File.ReadAllText(fallbackPath).Trim();
            Log($"Found device ID in fallback: {id}");
            return id;
        }
        
        var newId = Guid.NewGuid().ToString("N");
        Log($"⚠️ No device_id.txt found, generated new: {newId}");
        return newId;
    }
    
    static void Log(string message)
    {
        var logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
        Console.WriteLine(logLine);
        
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
