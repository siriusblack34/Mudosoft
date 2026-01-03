using System.Drawing;
using System.Drawing.Imaging;
using Microsoft.AspNetCore.SignalR.Client;
using Mudosoft.Shared.Dtos;

namespace MudoSoft.Tray;

/// <summary>
/// Remote Desktop Service - Runs in Tray (User Session)
/// Captures screen and sends to backend via SignalR
/// </summary>
public class RemoteDesktopService : IDisposable
{
    private HubConnection? _hubConnection;
    private readonly string _backendUrl;
    private readonly string _deviceId;
    private bool _isStreaming = false;
    private bool _isConnected = false;
    private CancellationTokenSource? _streamingCts;
    private readonly object _lock = new();
    private static readonly string _logPath = @"C:\ProgramData\MudoSoft\rd_service.log";
    
    public event Action<string>? OnLog;
    public event Action<bool>? OnConnectionChanged;
    
    public bool IsConnected => _isConnected;
    public bool IsStreaming => _isStreaming;

    public RemoteDesktopService(string backendUrl, string deviceId)
    {
        _backendUrl = backendUrl.TrimEnd('/');
        _deviceId = deviceId;
        
        // Ensure log directory exists
        var logDir = Path.GetDirectoryName(_logPath);
        if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
        {
            Directory.CreateDirectory(logDir);
        }
        
        FileLog($"=== RemoteDesktopService initialized ===");
        FileLog($"Backend URL: {_backendUrl}");
        FileLog($"Device ID: {_deviceId}");
    }

    private void FileLog(string message)
    {
        try
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n";
            File.AppendAllText(_logPath, line);
        }
        catch { }
    }

    public async Task StartAsync()
    {
        FileLog($"StartAsync called, connecting to: {_backendUrl}/hubs/desktop");
        Log($"Connecting to SignalR hub: {_backendUrl}/hubs/desktop");
        
        try
        {
            FileLog("Creating HubConnection...");
            _hubConnection = new HubConnectionBuilder()
                .WithUrl($"{_backendUrl}/hubs/desktop", options =>
                {
                    options.ApplicationMaxBufferSize = 5 * 1024 * 1024; // 5MB
                })
                .WithAutomaticReconnect()
                .Build();

            // Handle input events from remote viewer
            _hubConnection.On<InputEventDto>("PerformInput", HandleInput);
            
            // Handle streaming commands
            _hubConnection.On("StartStreaming", () =>
            {
                FileLog("🖥️ StartStreaming received!");
                Log("🖥️ Remote View: Streaming BAŞLATILDI");
                StartStreaming();
            });
            
            _hubConnection.On("StopStreaming", () =>
            {
                FileLog("🛑 StopStreaming received!");
                Log("🛑 Remote View: Streaming DURDURULDU");
                StopStreaming();
            });

            _hubConnection.Reconnecting += (ex) =>
            {
                FileLog($"⚠️ Reconnecting: {ex?.Message}");
                Log($"⚠️ Hub bağlantısı yeniden kuruluyor...");
                _isConnected = false;
                OnConnectionChanged?.Invoke(false);
                return Task.CompletedTask;
            };

            _hubConnection.Reconnected += async (connectionId) =>
            {
                FileLog($"✅ Reconnected: {connectionId}");
                Log($"✅ Hub yeniden bağlandı: {connectionId}");
                _isConnected = true;
                OnConnectionChanged?.Invoke(true);
                await RegisterDevice();
            };

            _hubConnection.Closed += (ex) =>
            {
                FileLog($"❌ Connection closed: {ex?.Message}");
                Log($"❌ Hub bağlantısı kapandı: {ex?.Message}");
                _isConnected = false;
                OnConnectionChanged?.Invoke(false);
                return Task.CompletedTask;
            };

            FileLog("Starting connection...");
            await _hubConnection.StartAsync();
            _isConnected = true;
            OnConnectionChanged?.Invoke(true);
            FileLog("✅ Connection successful!");
            Log($"✅ SignalR bağlantısı kuruldu!");
            
            await RegisterDevice();
        }
        catch (Exception ex)
        {
            FileLog($"❌ Connection error: {ex.Message}");
            Log($"❌ SignalR bağlantı hatası: {ex.Message}");
            _isConnected = false;
            OnConnectionChanged?.Invoke(false);
        }
    }

    private async Task RegisterDevice()
    {
        if (_hubConnection?.State == HubConnectionState.Connected)
        {
            try
            {
                await _hubConnection.InvokeAsync("RegisterDevice", _deviceId);
                Log($"📱 Cihaz kaydedildi: {_deviceId}");
            }
            catch (Exception ex)
            {
                Log($"Kayıt hatası: {ex.Message}");
            }
        }
    }

    private void StartStreaming()
    {
        lock (_lock)
        {
            if (_isStreaming) return;
            _isStreaming = true;
        }
        
        _streamingCts = new CancellationTokenSource();
        _ = StreamLoop(_streamingCts.Token);
    }

    private void StopStreaming()
    {
        lock (_lock)
        {
            _isStreaming = false;
        }
        _streamingCts?.Cancel();
        _streamingCts?.Dispose();
        _streamingCts = null;
    }

    private async Task StreamLoop(CancellationToken ct)
    {
        Log("📹 Streaming loop başlatıldı");
        
        while (!ct.IsCancellationRequested && _isStreaming)
        {
            try
            {
                await CaptureAndSendFrame();
                await Task.Delay(66, ct); // ~15 FPS
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log($"Frame hatası: {ex.Message}");
                await Task.Delay(500, ct);
            }
        }
        
        Log("📹 Streaming loop durduruldu");
    }

    private async Task CaptureAndSendFrame()
    {
        if (_hubConnection?.State != HubConnectionState.Connected) return;
        
        try
        {
            var bounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
            
            using var bitmap = new Bitmap(bounds.Width, bounds.Height);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
            }
            
            // Resize for bandwidth (75% of original for better quality)
            var newWidth = (int)(bounds.Width * 0.75);
            var newHeight = (int)(bounds.Height * 0.75);
            using var resized = new Bitmap(newWidth, newHeight);
            using (var g = Graphics.FromImage(resized))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                g.DrawImage(bitmap, 0, 0, newWidth, newHeight);
            }
            
            // Encode as JPEG with better quality
            using var ms = new MemoryStream();
            var encoder = GetEncoder(ImageFormat.Jpeg);
            var encoderParams = new EncoderParameters(1);
            encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, 70L); // 70% quality for better image
            resized.Save(ms, encoder, encoderParams);
            
            // Send to hub - use bytes for StreamFrame
            await _hubConnection.InvokeAsync("StreamFrame", _deviceId, ms.ToArray());
        }
        catch (Exception ex)
        {
            // Screen capture can fail if locked, etc.
            Log($"Capture error: {ex.Message}");
        }
    }

    private void HandleInput(InputEventDto input)
    {
        try
        {
            // Scale coordinates back to full screen (X,Y are 0.0-1.0 relative)
            var bounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
            int scaledX = (int)(input.X * bounds.Width);
            int scaledY = (int)(input.Y * bounds.Height);
            
            switch (input.Type)
            {
                case Mudosoft.Shared.Enums.InputEventType.MouseMove:
                    SetCursorPos(scaledX, scaledY);
                    break;
                    
                case Mudosoft.Shared.Enums.InputEventType.MouseDown:
                    SetCursorPos(scaledX, scaledY);
                    mouse_event(input.Button == 0 ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, 0);
                    break;
                    
                case Mudosoft.Shared.Enums.InputEventType.MouseUp:
                    SetCursorPos(scaledX, scaledY);
                    mouse_event(input.Button == 0 ? MOUSEEVENTF_LEFTUP : MOUSEEVENTF_RIGHTUP, 0, 0, 0, 0);
                    break;
                    
                case Mudosoft.Shared.Enums.InputEventType.KeyDown:
                case Mudosoft.Shared.Enums.InputEventType.KeyUp:
                    if (!string.IsNullOrEmpty(input.Key))
                    {
                        var vk = KeyCodeToVirtualKey(input.Key);
                        if (vk != 0)
                        {
                            keybd_event((byte)vk, 0, input.Type == Mudosoft.Shared.Enums.InputEventType.KeyUp ? KEYEVENTF_KEYUP : 0, 0);
                        }
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            Log($"Input error: {ex.Message}");
        }
    }

    private int KeyCodeToVirtualKey(string key)
    {
        // Basic key mapping
        if (key.Length == 1 && char.IsLetterOrDigit(key[0]))
        {
            return char.ToUpper(key[0]);
        }
        
        return key.ToLower() switch
        {
            "enter" => 0x0D,
            "escape" => 0x1B,
            "backspace" => 0x08,
            "tab" => 0x09,
            "space" => 0x20,
            "arrowup" => 0x26,
            "arrowdown" => 0x28,
            "arrowleft" => 0x25,
            "arrowright" => 0x27,
            "delete" => 0x2E,
            "home" => 0x24,
            "end" => 0x23,
            "f1" => 0x70,
            "f2" => 0x71,
            "f3" => 0x72,
            "f4" => 0x73,
            "f5" => 0x74,
            _ => 0
        };
    }

    private static ImageCodecInfo GetEncoder(ImageFormat format)
    {
        foreach (var codec in ImageCodecInfo.GetImageEncoders())
        {
            if (codec.FormatID == format.Guid)
                return codec;
        }
        return ImageCodecInfo.GetImageEncoders()[0];
    }

    private void Log(string message)
    {
        OnLog?.Invoke($"[RD] {message}");
        System.Diagnostics.Debug.WriteLine($"[RemoteDesktop] {message}");
    }

    public async Task StopAsync()
    {
        StopStreaming();
        
        if (_hubConnection != null)
        {
            await _hubConnection.StopAsync();
            await _hubConnection.DisposeAsync();
            _hubConnection = null;
        }
        
        _isConnected = false;
        OnConnectionChanged?.Invoke(false);
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
    }

    // Win32 API imports
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);
    
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void mouse_event(int dwFlags, int dx, int dy, int dwData, int dwExtraInfo);
    
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, int dwFlags, int dwExtraInfo);
    
    private const int MOUSEEVENTF_LEFTDOWN = 0x02;
    private const int MOUSEEVENTF_LEFTUP = 0x04;
    private const int MOUSEEVENTF_RIGHTDOWN = 0x08;
    private const int MOUSEEVENTF_RIGHTUP = 0x10;
    private const int KEYEVENTF_KEYUP = 0x02;
}
