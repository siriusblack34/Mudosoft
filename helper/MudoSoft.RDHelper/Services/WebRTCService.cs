using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Orchestra.Shared.Dtos;
using Orchestra.Shared.Enums;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.Encoders;

namespace Orchestra.RDHelper.Services;

/// <summary>
/// WebRTC Service for P2P screen streaming with VP8 encoding
/// Uses SIPSorcery for WebRTC peer connection and VP8 encoding
/// </summary>
public class WebRTCService : IDisposable
{
    private readonly string _deviceId;
    private readonly string _backendUrl;
    private readonly Rectangle _screenBounds;
    
    private HubConnection? _hubConnection;
    private RTCPeerConnection? _peerConnection;
    private RTCDataChannel? _inputChannel;
    private bool _isStreaming;
    private int _frameCount;
    private CancellationTokenSource? _cts;
    
    // VP8 video encoder endpoint
    private VpxVideoEncoder? _videoEncoder;
    private MediaStreamTrack? _videoTrack;
    
    // ICE servers (STUN + TURN on backend server)
    private readonly List<RTCIceServer> _iceServers = new()
    {
        new RTCIceServer { urls = "stun:stun.l.google.com:19302" },
        // TURN server on backend
        new RTCIceServer 
        { 
            urls = "turn:10.0.213.89:3478",
            username = "mudosoft",
            credential = "Mudo2024Turn!"
        },
        new RTCIceServer 
        { 
            urls = "turn:10.0.213.89:3478?transport=tcp",
            username = "mudosoft",
            credential = "Mudo2024Turn!"
        }
    };
    
    // Log path
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "MudoSoft", "rdhelper_webrtc.log");
    
    public event Action<string>? OnLog;
    
    public WebRTCService(string deviceId, string backendUrl, Rectangle screenBounds)
    {
        _deviceId = deviceId;
        _backendUrl = backendUrl;
        _screenBounds = screenBounds;
    }
    
    private void Log(string message)
    {
        var logLine = $"[{DateTime.Now:HH:mm:ss}] {message}";
        OnLog?.Invoke(logLine);
        
        try
        {
            var logDir = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
                Directory.CreateDirectory(logDir);
            File.AppendAllText(LogPath, logLine + Environment.NewLine);
        }
        catch { }
    }
    
    public async Task StartAsync()
    {
        Log("🚀 WebRTC Service starting...");
        
        // Initialize VP8 encoder
        _videoEncoder = new VpxVideoEncoder();
        Log("✅ VP8 encoder initialized");
        
        // Connect to signaling hub
        await ConnectToSignalingHubAsync();
        
        Log("✅ WebRTC Service ready, waiting for connection requests");
    }
    
    private async Task ConnectToSignalingHubAsync()
    {
        _hubConnection = new HubConnectionBuilder()
            .WithUrl($"{_backendUrl}/hubs/desktop")
            .WithAutomaticReconnect()
            .Build();
        
        // Handle signaling messages
        _hubConnection.On<string>("ReceiveOffer", async (offerSdp) =>
        {
            Log("📥 Received WebRTC offer from viewer");
            await HandleOfferAsync(offerSdp);
        });
        
        _hubConnection.On<string>("ReceiveIceCandidate", async (candidateJson) =>
        {
            Log("🧊 Received ICE candidate");
            await HandleIceCandidateAsync(candidateJson);
        });
        
        _hubConnection.On("StartStreaming", () =>
        {
            Log("▶️ StartStreaming command received (WebRTC mode)");
        });
        
        _hubConnection.On("StopStreaming", async () =>
        {
            Log("⏹️ StopStreaming command received");
            await StopStreamingAsync();
        });
        
        _hubConnection.Reconnected += async (connectionId) =>
        {
            Log($"🔄 Reconnected to hub: {connectionId}");
            await RegisterDeviceAsync();
        };
        
        await _hubConnection.StartAsync();
        Log("✅ Connected to signaling hub");
        
        await RegisterDeviceAsync();
    }
    
    private async Task RegisterDeviceAsync()
    {
        await _hubConnection!.InvokeAsync("RegisterDevice", _deviceId);
        Log($"📱 Registered device: {_deviceId}");
    }
    
    private async Task HandleOfferAsync(string offerSdp)
    {
        try
        {
            // Create new peer connection for each session
            await CreatePeerConnectionAsync();
            
            // Set remote description (offer from viewer)
            var offer = new RTCSessionDescriptionInit
            {
                type = RTCSdpType.offer,
                sdp = offerSdp
            };
            
            var result = _peerConnection!.setRemoteDescription(offer);
            if (result != SetDescriptionResultEnum.OK)
            {
                Log($"❌ Failed to set remote description: {result}");
                return;
            }
            Log("✅ Remote description set");
            
            // Create answer
            var answer = _peerConnection.createAnswer();
            await _peerConnection.setLocalDescription(answer);
            Log("✅ Local description set");
            
            // Send answer back to viewer
            await _hubConnection!.InvokeAsync("SendAnswer", _deviceId, answer.sdp);
            Log("📤 Sent WebRTC answer to viewer");
            
            // Start video stream immediately (don't wait for ICE to complete)
            // ICE negotiation continues in background
            Log("🎬 Starting video stream (ICE negotiating in background)...");
            StartVideoStream();
        }
        catch (Exception ex)
        {
            Log($"❌ Error handling offer: {ex.Message}");
        }
    }
    
    private async Task CreatePeerConnectionAsync()
    {
        Log("🔧 Creating WebRTC peer connection...");
        
        // Close existing connection if any
        if (_peerConnection != null)
        {
            _peerConnection.close();
            _peerConnection = null;
        }
        
        var config = new RTCConfiguration
        {
            iceServers = _iceServers
            // Don't force relay - try all methods
        };
        
        _peerConnection = new RTCPeerConnection(config);
        Log("✅ Peer connection created");
        
        // Create video track with VP8 (payload type 96 is standard for VP8)
        var videoFormat = new VideoFormat(VideoCodecsEnum.VP8, 96);
        _videoTrack = new MediaStreamTrack(videoFormat, MediaStreamStatusEnum.SendOnly);
        _peerConnection.addTrack(_videoTrack);
        Log("✅ Video track added: VP8");
        
        // Create data channel for input
        _inputChannel = await _peerConnection.createDataChannel("input");
        _inputChannel.onmessage += OnInputMessage;
        Log("📡 Data channel created for input");
        
        // Handle ICE candidates
        _peerConnection.onicecandidate += (candidate) =>
        {
            if (candidate != null)
            {
                var candidateJson = JsonSerializer.Serialize(new
                {
                    candidate = candidate.candidate,
                    sdpMid = candidate.sdpMid,
                    sdpMLineIndex = candidate.sdpMLineIndex
                });
                
                _hubConnection?.InvokeAsync("SendIceCandidate", _deviceId, candidateJson);
            }
        };
        
        // Handle connection state changes
        _peerConnection.onconnectionstatechange += (state) =>
        {
            Log($"🔗 Connection state: {state}");
            
            if (state == RTCPeerConnectionState.connected)
            {
                Log("✅ P2P connection established!");
                StartVideoStream();
            }
            else if (state == RTCPeerConnectionState.disconnected || 
                     state == RTCPeerConnectionState.failed ||
                     state == RTCPeerConnectionState.closed)
            {
                StopVideoStream();
            }
        };
        
        _peerConnection.oniceconnectionstatechange += (state) =>
        {
            Log($"🧊 ICE state: {state}");
            
            // Also start on ICE connected (some implementations use this instead)
            if (state == RTCIceConnectionState.connected)
            {
                Log("✅ ICE connected - starting video stream");
                StartVideoStream();
            }
        };
        
        Log("✅ Peer connection created");
        await Task.CompletedTask;
    }
    
    private async Task HandleIceCandidateAsync(string candidateJson)
    {
        try
        {
            if (_peerConnection == null) return;
            
            var candidateObj = JsonSerializer.Deserialize<JsonElement>(candidateJson);
            var candidate = new RTCIceCandidateInit
            {
                candidate = candidateObj.GetProperty("candidate").GetString(),
                sdpMid = candidateObj.GetProperty("sdpMid").GetString(),
                sdpMLineIndex = (ushort)candidateObj.GetProperty("sdpMLineIndex").GetInt32()
            };
            
            _peerConnection.addIceCandidate(candidate);
            Log("✅ Added ICE candidate");
        }
        catch (Exception ex)
        {
            Log($"❌ Error adding ICE candidate: {ex.Message}");
        }
    }
    
    private void StartVideoStream()
    {
        if (_isStreaming) return;
        _isStreaming = true;
        _cts = new CancellationTokenSource();
        _frameCount = 0;
        
        Task.Run(() => VideoStreamLoopAsync(_cts.Token));
        Log("🎬 Video streaming started");
    }
    
    private void StopVideoStream()
    {
        if (!_isStreaming) return;
        _isStreaming = false;
        _cts?.Cancel();
        Log($"🎬 Video streaming stopped after {_frameCount} frames");
    }
    
    private async Task VideoStreamLoopAsync(CancellationToken ct)
    {
        var targetFps = 15;
        var frameDelay = 1000 / targetFps;
        uint timestamp = 0;
        var timestampIncrement = (uint)(90000 / targetFps); // 90kHz clock for video
        
        Log($"🎬 Starting video loop: {targetFps} FPS, {_screenBounds.Width}x{_screenBounds.Height}");
        
        while (_isStreaming && !ct.IsCancellationRequested)
        {
            try
            {
                var startTime = DateTime.UtcNow;
                
                // Capture screen as raw BGR24
                var rawFrame = CaptureScreenRaw();
                if (rawFrame != null && _videoTrack != null && _peerConnection != null)
                {
                    // Encode frame to VP8
                    if (_videoEncoder == null)
                    {
                        if (_frameCount == 0)
                        {
                            Log("❌ VP8 encoder is null!");
                        }
                    }
                    else
                    {
                        var encodedSamples = _videoEncoder.EncodeVideo(
                            _screenBounds.Width,
                            _screenBounds.Height,
                            rawFrame,
                            VideoPixelFormatsEnum.Bgr,
                            VideoCodecsEnum.VP8);
                        
                        if (encodedSamples != null && encodedSamples.Length > 0)
                        {
                            // Send encoded frame via RTP
                            _peerConnection.SendVideo(timestamp, encodedSamples);
                            
                            _frameCount++;
                            if (_frameCount % 30 == 1)
                            {
                                Log($"🖼️ Frame #{_frameCount} sent, encoded: {encodedSamples.Length} bytes (raw: {rawFrame.Length})");
                            }
                        }
                        else
                        {
                            if (_frameCount == 0)
                            {
                                Log($"⚠️ VP8 encoding returned null/empty for frame, raw size: {rawFrame.Length}");
                            }
                        }
                    }
                    
                    timestamp += timestampIncrement;
                }
                
                // Maintain frame rate
                var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                var delay = Math.Max(1, frameDelay - (int)elapsed);
                await Task.Delay(delay, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log($"❌ Stream error: {ex.Message}");
                await Task.Delay(100, ct);
            }
        }
        
        Log($"🎬 Video loop ended after {_frameCount} frames");
    }
    
    /// <summary>
    /// Capture screen as raw BGR24 byte array for VP8 encoding
    /// </summary>
    private byte[]? CaptureScreenRaw()
    {
        try
        {
            using var bitmap = new Bitmap(_screenBounds.Width, _screenBounds.Height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.CopyFromScreen(_screenBounds.Location, Point.Empty, _screenBounds.Size);
            }
            
            // Lock bits to get raw pixel data
            var bmpData = bitmap.LockBits(
                new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format24bppRgb);
            
            try
            {
                int bytes = Math.Abs(bmpData.Stride) * bitmap.Height;
                byte[] rgbValues = new byte[bytes];
                Marshal.Copy(bmpData.Scan0, rgbValues, 0, bytes);
                return rgbValues;
            }
            finally
            {
                bitmap.UnlockBits(bmpData);
            }
        }
        catch (Exception ex)
        {
            Log($"❌ Capture error: {ex.Message}");
            return null;
        }
    }
    
    private void OnInputMessage(RTCDataChannel dc, DataChannelPayloadProtocols protocol, byte[] data)
    {
        try
        {
            var json = Encoding.UTF8.GetString(data);
            var input = JsonSerializer.Deserialize<InputEventDto>(json, new JsonSerializerOptions 
            { 
                PropertyNameCaseInsensitive = true 
            });
            
            if (input != null)
            {
                HandleInput(input);
            }
        }
        catch (Exception ex)
        {
            Log($"❌ Input error: {ex.Message}");
        }
    }
    
    private void HandleInput(InputEventDto input)
    {
        int screenX = (int)(input.X * _screenBounds.Width);
        int screenY = (int)(input.Y * _screenBounds.Height);
        
        switch (input.Type)
        {
            case InputEventType.MouseMove:
                SetCursorPos(screenX, screenY);
                break;
                
            case InputEventType.MouseDown:
                SetCursorPos(screenX, screenY);
                mouse_event(GetMouseDownFlag(input.Button), 0, 0, 0, 0);
                break;
                
            case InputEventType.MouseUp:
                SetCursorPos(screenX, screenY);
                mouse_event(GetMouseUpFlag(input.Button), 0, 0, 0, 0);
                break;
                
            case InputEventType.KeyDown:
                HandleKeyDown(input);
                break;
                
            case InputEventType.KeyUp:
                HandleKeyUp(input);
                break;
                
            case InputEventType.Scroll:
                SetCursorPos(screenX, screenY);
                int scrollAmount = (int)(input.Y * 120 * 3);
                mouse_event(MOUSEEVENTF_WHEEL, 0, 0, (uint)scrollAmount, 0);
                break;
        }
    }
    
    private void HandleKeyDown(InputEventDto input)
    {
        if (string.IsNullOrEmpty(input.Key)) return;
        
        var vk = GetVirtualKeyCode(input.Key);
        
        if (vk != 0)
        {
            if (input.Ctrl) keybd_event(0x11, 0, 0, 0);
            if (input.Shift) keybd_event(0x10, 0, 0, 0);
            if (input.Alt) keybd_event(0x12, 0, 0, 0);
            keybd_event((byte)vk, 0, 0, 0);
        }
        else if (input.Key.Length == 1)
        {
            // Unicode character support (Turkish etc.)
            SendUnicodeChar(input.Key[0], false);
        }
    }
    
    private void HandleKeyUp(InputEventDto input)
    {
        if (string.IsNullOrEmpty(input.Key)) return;
        
        var vk = GetVirtualKeyCode(input.Key);
        
        if (vk != 0)
        {
            keybd_event((byte)vk, 0, KEYEVENTF_KEYUP, 0);
            if (input.Ctrl) keybd_event(0x11, 0, KEYEVENTF_KEYUP, 0);
            if (input.Shift) keybd_event(0x10, 0, KEYEVENTF_KEYUP, 0);
            if (input.Alt) keybd_event(0x12, 0, KEYEVENTF_KEYUP, 0);
        }
        else if (input.Key.Length == 1)
        {
            SendUnicodeChar(input.Key[0], true);
        }
    }
    
    private void SendUnicodeChar(char c, bool keyUp)
    {
        var inputs = new INPUT[1];
        inputs[0].type = INPUT_KEYBOARD;
        inputs[0].ki.wVk = 0;
        inputs[0].ki.wScan = c;
        inputs[0].ki.dwFlags = KEYEVENTF_UNICODE | (keyUp ? KEYEVENTF_KEYUP : 0);
        inputs[0].ki.time = 0;
        inputs[0].ki.dwExtraInfo = IntPtr.Zero;
        
        SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
    }
    
    private async Task StopStreamingAsync()
    {
        StopVideoStream();
        
        if (_peerConnection != null)
        {
            _peerConnection.close();
            _peerConnection = null;
        }
        
        await Task.CompletedTask;
    }
    
    public void Dispose()
    {
        _cts?.Cancel();
        _peerConnection?.close();
        _hubConnection?.DisposeAsync();
        _videoEncoder?.Dispose();
    }
    
    #region P/Invoke
    
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const int INPUT_KEYBOARD = 1;
    
    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int X, int Y);
    
    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, int dwExtraInfo);
    
    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);
    
    [DllImport("user32.dll")]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
    
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public KEYBDINPUT ki;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
        public uint unused1;
        public uint unused2;
    }
    
    private uint GetMouseDownFlag(int button) => button switch
    {
        2 => MOUSEEVENTF_RIGHTDOWN,
        1 => MOUSEEVENTF_MIDDLEDOWN,
        _ => MOUSEEVENTF_LEFTDOWN
    };
    
    private uint GetMouseUpFlag(int button) => button switch
    {
        2 => MOUSEEVENTF_RIGHTUP,
        1 => MOUSEEVENTF_MIDDLEUP,
        _ => MOUSEEVENTF_LEFTUP
    };
    
    private int GetVirtualKeyCode(string key) => key.ToUpperInvariant() switch
    {
        "ENTER" => 0x0D,
        "ESCAPE" => 0x1B,
        "TAB" => 0x09,
        "BACKSPACE" => 0x08,
        "DELETE" => 0x2E,
        "SPACE" or " " => 0x20,
        "ARROWUP" => 0x26,
        "ARROWDOWN" => 0x28,
        "ARROWLEFT" => 0x25,
        "ARROWRIGHT" => 0x27,
        "HOME" => 0x24,
        "END" => 0x23,
        "PAGEUP" => 0x21,
        "PAGEDOWN" => 0x22,
        "INSERT" => 0x2D,
        "F1" => 0x70,
        "F2" => 0x71,
        "F3" => 0x72,
        "F4" => 0x73,
        "F5" => 0x74,
        "F6" => 0x75,
        "F7" => 0x76,
        "F8" => 0x77,
        "F9" => 0x78,
        "F10" => 0x79,
        "F11" => 0x7A,
        "F12" => 0x7B,
        "CONTROL" => 0x11,
        "SHIFT" => 0x10,
        "ALT" => 0x12,
        _ => 0
    };
    
    #endregion
}
