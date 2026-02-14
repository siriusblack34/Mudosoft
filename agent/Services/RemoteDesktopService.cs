using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mudosoft.Agent.Interfaces;
using Mudosoft.Agent.Models;
using Mudosoft.Agent.Helpers;
using Mudosoft.Shared.Dtos;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Diagnostics; // Added for Process

namespace Mudosoft.Agent.Services;

[SupportedOSPlatform("windows")]
public class RemoteDesktopService : BackgroundService
{
    private readonly ILogger<RemoteDesktopService> _logger;
    private readonly IDeviceIdentityProvider _identityProvider;
    private readonly AgentConfig _config;
    private HubConnection? _hubConnection;
    private bool _isStreaming = false;
    private readonly int _targetWidth = 1600; 
    private readonly int _jpegQuality = 75; 
    private readonly InputSimulator _inputSimulator;
    
    private readonly RemoteDesktopConfig _rdConfig;
    private Process? _helperProcess;
    
    // Monitor selection: -1 = all monitors (virtual screen), 0+ = specific monitor index
    private int _selectedMonitor = -1;

    public RemoteDesktopService(
        ILogger<RemoteDesktopService> logger,
        IDeviceIdentityProvider identityProvider,
        IOptions<AgentConfig> config,
        RemoteDesktopConfig rdConfig) // Inject Mode
    {
        _logger = logger;
        _identityProvider = identityProvider;
        _config = config.Value;
        _rdConfig = rdConfig;
        _inputSimulator = new InputSimulator();
    }

    private void LogDebug(string message)
    {
        try
        {
            File.AppendAllText(@"C:\mudosoft_debug.log", $"{DateTime.Now}: {message}{Environment.NewLine}");
        }
        catch { }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogDebug($"ExecuteAsync Started. Mode: {_rdConfig.Mode}");
        if (_rdConfig.Mode == "Helper")
        {
            await RunHelperMode(stoppingToken);
        }
        else
        {
            await RunManagerMode(stoppingToken);
        }
    }

    // --- HELPER MODE (User Session) ---
    private async Task RunHelperMode(CancellationToken stoppingToken)
    {
        LogDebug("Helper Mode initializing...");
        
        // Immediately write heartbeat so manager knows we're alive
        UpdateHelperHeartbeat();
        LogDebug("Heartbeat written immediately");
        
        try
        {
            var deviceId = _identityProvider.GetDeviceId();
            var hubUrl = $"{_config.BackendUrl}/hubs/desktop";
            LogDebug($"Connecting to hub: {hubUrl} with deviceId: {deviceId}");

            _hubConnection = new HubConnectionBuilder()
                .WithUrl(hubUrl, options =>
                {
                    options.ApplicationMaxBufferSize = 5 * 1024 * 1024; // 5MB
                })
                .WithAutomaticReconnect()
                .Build();

        // 0. Input Listener
        _hubConnection.On<InputEventDto>("PerformInput", (input) =>
        {
            try
            {
                _inputSimulator.HandleInput(input);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Input error: {Message}", ex.Message);
            }
        });

        // 1. Dinleyiciler
        _hubConnection.On("StartStreaming", () =>
        {
            _logger.LogInformation("🖥️ Uzak masaüstü yayını BAŞLATILIYOR...");
            _isStreaming = true;
        });

        _hubConnection.On("StopStreaming", () =>
        {
            _logger.LogInformation("⏹️ Uzak masaüstü yayını DURDURULDU.");
            _isStreaming = false;
        });

        // Monitor selection handler (-1 = all, 0+ = specific monitor)
        _hubConnection.On<int>("SelectMonitor", (monitorIndex) =>
        {
            _selectedMonitor = monitorIndex;
            _logger.LogInformation("🖥️ Monitör seçildi: {Monitor}", monitorIndex == -1 ? "Tümü" : $"Monitör {monitorIndex + 1}");
        });

        // 2. Bağlan
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_hubConnection.State == HubConnectionState.Disconnected)
                {
                    await _hubConnection.StartAsync(stoppingToken);
                    _logger.LogInformation("📡 RemoteDesktop Helper Hub'a bağlanıldı!");
                    
                    // Kendimizi kaydettirelim
                    await _hubConnection.InvokeAsync("RegisterDevice", deviceId, stoppingToken);

                    // Helper başladığında otomatik yayına başlasın mı? 
                    // Backend "StartStreaming" gönderene kadar bekleyebiliriz ama 
                    // basitlik için Helper açıldığı an yayına hazır olsun.
                    _isStreaming = true; 
                }

                // 3. Streaming Döngüsü
                if (_isStreaming && _hubConnection.State == HubConnectionState.Connected)
                {
                    // Helper çalıştığını bildir (flag dosyası)
                    UpdateHelperHeartbeat();
                    
                    await CaptureAndSendFrame(deviceId);
                    await Task.Delay(10, stoppingToken); // Minimize latency
                }
                else
                {
                    // Streaming değilken de heartbeat gönder (Manager bekleyebilir)
                    UpdateHelperHeartbeat();
                    await Task.Delay(1000, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Helper loop error: {ex.Message}");
                _logger.LogError(ex, "RemoteDesktop Helper hatası");
                _isStreaming = false; 
                UpdateHelperHeartbeat(); // Keep heartbeat even on error
                await Task.Delay(5000, stoppingToken);
            }
        }
        }
        catch (Exception ex)
        {
            LogDebug($"Helper FATAL error: {ex}");
            _logger.LogError(ex, "Helper fatal error");
        }
    }

    // --- MANAGER MODE (Service Session 0) ---
    private async Task RunManagerMode(CancellationToken stoppingToken)
    {
        LogDebug("🛡️ RemoteDesktop Manager başlatıldı. Oturum bekleniyor...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Helper zaten çalışıyor mu kontrol et (flag dosyasıyla)
                if (!IsHelperRunning())
                {
                    // Aktif oturum var mı?
                    try 
                    {
                        uint sessionId = SessionInterop.WTSGetActiveConsoleSessionId();
                        if (sessionId != 0xFFFFFFFF)
                        {
                            LogDebug($"👤 Aktif Session bulundu: {sessionId}. Helper başlatılıyor...");
                            LaunchHelper();
                        }
                    }
                    catch (Exception ex) 
                    {
                         LogDebug($"Session Check Error: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Manager Loop Error: {ex.Message}");
            }

            await Task.Delay(10000, stoppingToken); // 10 saniyeye çıkar
        }
    }
    
    private static readonly string HelperFlagPath = @"C:\MudoSoftAgent\helper_running.flag";
    
    private bool IsHelperRunning()
    {
        try
        {
            // Flag dosyası var mı ve son 60 saniye içinde güncellendi mi?
            if (File.Exists(HelperFlagPath))
            {
                var lastWrite = File.GetLastWriteTime(HelperFlagPath);
                if ((DateTime.Now - lastWrite).TotalSeconds < 60)
                {
                    return true; // Helper çalışıyor
                }
            }
            return false;
        }
        catch
        {
            return false;
        }
    }
    
    private void UpdateHelperHeartbeat()
    {
        try
        {
            // Flag dosyasını güncelle (varlığını ve son yazma zamanını)
            File.WriteAllText(HelperFlagPath, DateTime.Now.ToString("o"));
        }
        catch { }
    }

    private void LaunchHelper()
    {
        try
        {
            string exePath = Environment.ProcessPath!;
            LogDebug($"Launching Helper: {exePath} --desktop-helper");
            
            SessionInterop.CreateProcessInConsoleSession($"\"{exePath}\" --desktop-helper");
            
            LogDebug("🚀 Helper process başlatıldı!");
        }
        catch (Exception ex)
        {
            LogDebug($"Helper Launch FAILED: {ex}");
        }
    }

    private async Task CaptureAndSendFrame(string deviceId)
    {
        // ... (Existing Logic)
        try
        {
            if (!OperatingSystem.IsWindows()) return;

            var bounds = GetScreenBounds();
            using var bitmap = new Bitmap(bounds.Width, bounds.Height);
            using (var g = Graphics.FromImage(bitmap))
            {
                // Use bounds.Location for correct capture of specific monitors
                g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
            }

            using var resized = ResizeImage(bitmap, _targetWidth);
            
            using var stream = new MemoryStream();
            SaveJpeg(resized, stream, _jpegQuality);
            
            var imageBytes = stream.ToArray();

            if (_hubConnection != null)
            {
                await _hubConnection.InvokeAsync("StreamFrame", deviceId, imageBytes);
            }
        }
        catch (Exception ex)
        {
            // _logger.LogWarning("Ekran yakalama başarısız: {Message}", ex.Message);
        }
    }
    // ... (Helper Methods: GetScreenBounds, ResizeImage, SaveJpeg...)
    
    // P/Invoke for monitor enumeration (avoiding Windows Forms dependency)
    private delegate bool MonitorEnumDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);
    
    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumDelegate lpfnEnum, IntPtr dwData);
    
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
    
    private static List<Rectangle> _monitorBounds = new();
    
    private void RefreshMonitorList()
    {
        _monitorBounds.Clear();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData) =>
        {
            _monitorBounds.Add(new Rectangle(
                lprcMonitor.Left,
                lprcMonitor.Top,
                lprcMonitor.Right - lprcMonitor.Left,
                lprcMonitor.Bottom - lprcMonitor.Top));
            return true;
        }, IntPtr.Zero);
    }
    
    private Rectangle GetScreenBounds()
    {
        // If specific monitor selected, enumerate monitors
        if (_selectedMonitor >= 0)
        {
            RefreshMonitorList();
            if (_selectedMonitor < _monitorBounds.Count)
            {
                return _monitorBounds[_selectedMonitor];
            }
            // Fallback to first monitor if index out of range
            if (_monitorBounds.Count > 0)
            {
                return _monitorBounds[0];
            }
        }
        
        // Default: all monitors (virtual screen)
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
    
    // Helper to get monitor count for frontend
    public static int GetMonitorCount()
    {
        var count = 0;
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData) =>
        {
            count++;
            return true;
        }, IntPtr.Zero);
        return count;
    }

    private Bitmap ResizeImage(Bitmap image, int width)
    {
        int height = (int)((double)image.Height * width / image.Width);
        var destRect = new Rectangle(0, 0, width, height);
        var destImage = new Bitmap(width, height);
        destImage.SetResolution(image.HorizontalResolution, image.VerticalResolution);
        using (var graphics = Graphics.FromImage(destImage))
        {
            graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Low;
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighSpeed;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighSpeed;
            using (var wrapMode = new ImageAttributes())
            {
                wrapMode.SetWrapMode(System.Drawing.Drawing2D.WrapMode.TileFlipXY);
                graphics.DrawImage(image, destRect, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, wrapMode);
            }
        }
        return destImage;
    }

    private void SaveJpeg(Bitmap image, MemoryStream stream, long quality)
    {
        var jpgEncoder = GetEncoder(ImageFormat.Jpeg);
        var myEncoder = System.Drawing.Imaging.Encoder.Quality;
        var myEncoderParameters = new EncoderParameters(1);
        var myEncoderParameter = new EncoderParameter(myEncoder, quality);
        myEncoderParameters.Param[0] = myEncoderParameter;
        image.Save(stream, jpgEncoder, myEncoderParameters);
    }

    private ImageCodecInfo GetEncoder(ImageFormat format)
    {
        ImageCodecInfo[] codecs = ImageCodecInfo.GetImageEncoders();
        return codecs.FirstOrDefault(codec => codec.FormatID == format.Guid) ?? codecs[0];
    }
}
