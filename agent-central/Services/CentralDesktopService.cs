using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestra.CentralAgent.Forms;
using Orchestra.Shared.Dtos;
using System.Drawing;
using System.Drawing.Imaging;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows.Forms;

namespace Orchestra.CentralAgent.Services;

/// <summary>
/// Kullanıcı oturumunda çalışan merkez ajan servisi.
/// SignalR üzerinden:
///   - Onay isteklerini dialog ile kullanıcıya iletir
///   - Bağlantı overlay'ini gösterir/gizler
///   - Input lock yönetir
///   - Kilit ekranı kontrolü yapar
/// </summary>
[SupportedOSPlatform("windows")]
public class CentralDesktopService : BackgroundService
{
    private readonly ILogger<CentralDesktopService> _logger;
    private readonly CentralAgentConfig _config;
    private readonly DeviceIdentityService _identity;
    private HubConnection? _hub;
    private ConnectionOverlayForm? _overlay;
    private Thread? _overlayThread;
    private volatile bool _inputLocked;
    private IntPtr _kbHook  = IntPtr.Zero;
    private IntPtr _mHook   = IntPtr.Zero;
    private LowLevelProc? _kbProc;
    private LowLevelProc? _mProc;
    private ConsentForm? _currentConsentForm; // aktif onay diyalogu (iptal için)

    public CentralDesktopService(
        ILogger<CentralDesktopService> logger,
        IOptions<CentralAgentConfig> config,
        DeviceIdentityService identity)
    {
        _logger   = logger;
        _config   = config.Value;
        _identity = identity;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var deviceId = _identity.GetDeviceId();

        _hub = new HubConnectionBuilder()
            .WithUrl($"{_config.BackendUrl}/hubs/desktop")
            .WithAutomaticReconnect(new[]
            {
                TimeSpan.Zero,
                TimeSpan.FromSeconds(3),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(30)
            })
            .Build();

        _hub.Reconnected += async _ =>
        {
            await _hub.InvokeAsync("RegisterDevice", deviceId, stoppingToken);
        };

        // ── Onay İptali — yeni bağlantı isteği geldiğinde eski diyalogu kapat ──
        _hub.On("CancelConsent", () =>
        {
            _logger.LogInformation("CancelConsent alındı — açık diyalog kapatılıyor");
            try { _currentConsentForm?.Invoke(() => { _currentConsentForm?.Close(); }); } catch { }
            _currentConsentForm = null;
        });

        // ── Onay İsteği ─────────────────────────────────────────────────
        _hub.On<string, string, string>("RequestConsent",
            (requestId, requesterName, requesterUsername) =>
        {
            _logger.LogInformation("Onay isteği: {ReqId} / {Tech}", requestId, requesterName);
            AppendHelperLog($"RequestConsent: {requestId} / {requesterName}");

            bool locked = IsScreenLocked();
            AppendHelperLog($"IsScreenLocked={locked}");
            if (locked)
            {
                _logger.LogInformation("Ekran kilitli — onay reddediliyor");
                _ = _hub.InvokeAsync("SubmitConsentResponse", requestId, false, "lockscreen");
                return;
            }

            // Kullanıcıya dialog göster
            var hub         = _hub;
            var backendUrl  = _config.BackendUrl;
            var thread = new Thread(() =>
            {
                AppendHelperLog("STA thread: ConsentForm gösteriliyor");
                Application.EnableVisualStyles();
                var dlg = new ConsentForm(requesterName, requesterUsername);
                _currentConsentForm = dlg;
                var result = dlg.ShowDialog();
                _currentConsentForm = null;
                bool approved = result == DialogResult.Yes;
                AppendHelperLog($"ConsentForm kapandı: approved={approved}");
                _logger.LogInformation("Kullanıcı onay cevabı: {Approved}", approved);

                // HTTP doğrudan — SignalR'a güvenme, her zaman HTTP kullan (3 deneme)
                Task.Run(async () =>
                {
                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        try
                        {
                            using var http = new System.Net.Http.HttpClient
                            {
                                BaseAddress = new Uri(backendUrl),
                                Timeout = TimeSpan.FromSeconds(8)
                            };
                            var url = $"/api/rdp/consent-response/{requestId}?approved={approved}";
                            var resp = await http.PostAsync(url, null);
                            _logger.LogInformation("ConsentResponse HTTP {Status} (deneme {N})", resp.StatusCode, attempt + 1);
                            if (resp.IsSuccessStatusCode) return;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning("ConsentResponse deneme {N} hata: {Msg}", attempt + 1, ex.Message);
                        }
                        if (attempt < 2) await Task.Delay(1000);
                    }
                }).Wait(TimeSpan.FromSeconds(30));
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
        });

        // ── Overlay ──────────────────────────────────────────────────────
        _hub.On<string, string>("ShowOverlay", (techName, techIp) =>
        {
            HideOverlay();
            _overlayThread = new Thread(() =>
            {
                Application.EnableVisualStyles();
                _overlay = new ConnectionOverlayForm(techName, techIp);
                _overlay.ShowDialog();
            });
            _overlayThread.SetApartmentState(ApartmentState.STA);
            _overlayThread.IsBackground = true;
            _overlayThread.Start();
        });

        _hub.On("HideOverlay", () =>
        {
            HideOverlay();
            DisableInputLock();
        });

        // ── Input Lock ───────────────────────────────────────────────────
        _hub.On<bool>("SetInputLock", locked =>
        {
            if (locked) EnableInputLock();
            else        DisableInputLock();
        });

        // ── Bağlan + Kayıt ───────────────────────────────────────────────
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_hub.State == HubConnectionState.Disconnected)
                {
                    await _hub.StartAsync(stoppingToken);
                    await _hub.InvokeAsync("RegisterDevice", deviceId, stoppingToken);
                    _logger.LogInformation("Hub bağlı, device={DeviceId}", deviceId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Hub bağlantı hatası: {Msg}", ex.Message);
            }

            await Task.Delay(10_000, stoppingToken).ContinueWith(_ => { });
        }
    }

    private void HideOverlay()
    {
        if (_overlay == null) return;
        try { _overlay.Invoke(() => { _overlay.Close(); _overlay.Dispose(); }); } catch { }
        _overlay = null;
    }

    // ── Input Lock ───────────────────────────────────────────────────────

    private delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int id, LowLevelProc fn, IntPtr hMod, uint tid);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hk, int n, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandle(string? n);

    private void EnableInputLock()
    {
        if (_inputLocked) return;
        _inputLocked = true;
        var hMod = GetModuleHandle(null);
        _kbProc = BlockHook; _mProc = BlockHook;
        _kbHook = SetWindowsHookEx(13, _kbProc, hMod, 0);
        _mHook  = SetWindowsHookEx(14, _mProc,  hMod, 0);
    }

    private void DisableInputLock()
    {
        if (!_inputLocked) return;
        _inputLocked = false;
        if (_kbHook != IntPtr.Zero) { UnhookWindowsHookEx(_kbHook); _kbHook = IntPtr.Zero; }
        if (_mHook  != IntPtr.Zero) { UnhookWindowsHookEx(_mHook);  _mHook  = IntPtr.Zero; }
    }

    private IntPtr BlockHook(int n, IntPtr w, IntPtr l) =>
        (_inputLocked && n >= 0) ? new IntPtr(1) : CallNextHookEx(IntPtr.Zero, n, w, l);

    // ── Helper Log (dosya) ──────────────────────────────────────────────
    private static readonly string _logPath = @"C:\ProgramData\OrchestraCentralAgent\helper.log";

    private static void AppendHelperLog(string msg)
    {
        try
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {msg}{Environment.NewLine}";
            File.AppendAllText(_logPath, line);
        }
        catch { }
    }

    // ── Lock Screen Detection ────────────────────────────────────────────
    // Helper kullanıcı oturumunda çalışır; kendi sessionId'si üzerinden
    // LogonUI.exe varlığını kontrol eder.

    [DllImport("kernel32.dll")] private static extern uint WTSGetActiveConsoleSessionId();

    private static bool IsScreenLocked()
    {
        try
        {
            // Aktif konsol session'ında LogonUI.exe varsa ekran kilitlidir
            uint activeSession = WTSGetActiveConsoleSessionId();
            return System.Diagnostics.Process
                .GetProcessesByName("LogonUI")
                .Any(p => (uint)p.SessionId == activeSession);
        }
        catch
        {
            return false;
        }
    }
}
