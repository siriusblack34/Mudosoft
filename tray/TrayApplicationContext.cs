using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using System.Diagnostics;
using System.Net.NetworkInformation;

namespace Orchestra.Tray;

public class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly ContextMenuStrip _contextMenu;
    private readonly System.Windows.Forms.Timer _statusTimer;
    
    private AgentStatus _currentStatus = AgentStatus.Disconnected;
    private AgentStatus _previousStatus = AgentStatus.Disconnected;
    private string _agentVersion = "?";
    private string _deviceId = "?";
    private string _backendUrl = "http://10.0.213.89:5102"; // Default, will be updated from Agent
    private DateTime _lastHeartbeat = DateTime.MinValue;
    private bool _firstCheck = true;
    
    // Remote Desktop Service
    private RemoteDesktopService? _remoteDesktopService;
    private bool _rdServiceStarted = false;

    public TrayApplicationContext()
    {
        // Ensure auto-start on Windows startup
        EnsureAutoStart();
        
        // Create context menu
        _contextMenu = CreateContextMenu();

        // Create tray icon
        _trayIcon = new NotifyIcon
        {
            Icon = CreateStatusIcon(_currentStatus),
            Text = "Orchestra - Başlatılıyor...",
            Visible = true,
            ContextMenuStrip = _contextMenu
        };

        _trayIcon.DoubleClick += (s, e) => ShowStatusWindow();

        // Status check timer
        _statusTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        _statusTimer.Tick += async (s, e) => await CheckAgentStatusAsync();
        _statusTimer.Start();

        // Initial check
        _ = CheckAgentStatusAsync();
        
        // Show startup notification
        _trayIcon.ShowBalloonTip(2000, "Orchestra", "Endpoint koruması aktif", ToolTipIcon.Info);
    }
    
    private async Task StartRemoteDesktopService()
    {
        if (_rdServiceStarted || _deviceId == "?" || string.IsNullOrEmpty(_deviceId)) return;
        
        try
        {
            _remoteDesktopService = new RemoteDesktopService(_backendUrl, _deviceId);
            _remoteDesktopService.OnLog += (msg) => Debug.WriteLine(msg);
            _remoteDesktopService.OnConnectionChanged += (connected) =>
            {
                Debug.WriteLine($"Remote Desktop connection: {connected}");
            };
            
            await _remoteDesktopService.StartAsync();
            _rdServiceStarted = true;
            Debug.WriteLine("🖥️ Remote Desktop Service started!");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Remote Desktop Service error: {ex.Message}");
        }
    }
    
    private async Task CheckForUpdates()
    {
        _trayIcon.ShowBalloonTip(2000, "Güncelleme", "Güncelleme kontrol ediliyor...", ToolTipIcon.Info);
        
        try
        {
            var updateService = new UpdateService(_backendUrl);
            var (hasUpdate, version) = await updateService.CheckForUpdateAsync();
            
            if (hasUpdate && !string.IsNullOrEmpty(version))
            {
                var result = MessageBox.Show(
                    $"Yeni versiyon mevcut: {version}\n\nGüncellemek istiyor musunuz?\n\nBu işlem Agent servisini ve Tray uygulamasını yeniden başlatacak.",
                    "Güncelleme Mevcut",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                
                if (result == DialogResult.Yes)
                {
                    updateService.OnLog += (msg) => Debug.WriteLine(msg);
                    updateService.OnComplete += (success, message) =>
                    {
                        if (!success)
                        {
                            _trayIcon.ShowBalloonTip(3000, "Güncelleme Hatası", message, ToolTipIcon.Error);
                        }
                    };
                    
                    await updateService.PerformUpdateAsync();
                }
            }
            else
            {
                _trayIcon.ShowBalloonTip(2000, "Güncelleme", "En güncel versiyon kullanılıyor ✓", ToolTipIcon.Info);
            }
        }
        catch (Exception ex)
        {
            _trayIcon.ShowBalloonTip(3000, "Hata", $"Güncelleme kontrolü başarısız: {ex.Message}", ToolTipIcon.Error);
        }
    }

    private void EnsureAutoStart()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            if (key != null)
            {
                var currentPath = Application.ExecutablePath;
                var existingValue = key.GetValue("MudoSoftTray") as string;
                
                if (existingValue != currentPath)
                {
                    key.SetValue("MudoSoftTray", currentPath);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Auto-start registry error: {ex.Message}");
        }
    }

    private ContextMenuStrip CreateContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Renderer = new ModernMenuRenderer();

        // Status header (disabled, just for display)
        var statusItem = new ToolStripMenuItem("● Durum: Kontrol ediliyor...")
        {
            Enabled = false,
            Name = "statusItem"
        };
        menu.Items.Add(statusItem);

        menu.Items.Add(new ToolStripSeparator());

        // Version info
        var versionItem = new ToolStripMenuItem("Versiyon: -")
        {
            Enabled = false,
            Name = "versionItem"
        };
        menu.Items.Add(versionItem);

        // Device ID
        var deviceItem = new ToolStripMenuItem("Cihaz: -")
        {
            Enabled = false,
            Name = "deviceItem"
        };
        menu.Items.Add(deviceItem);

        // Last heartbeat
        var heartbeatItem = new ToolStripMenuItem("Son Bağlantı: -")
        {
            Enabled = false,
            Name = "heartbeatItem"
        };
        menu.Items.Add(heartbeatItem);

        menu.Items.Add(new ToolStripSeparator());

        // Quick Actions submenu
        var actionsMenu = new ToolStripMenuItem("⚡ Hızlı İşlemler");
        actionsMenu.DropDownItems.Add("🔄 Servisi Yeniden Başlat", null, (s, e) => RestartService());
        actionsMenu.DropDownItems.Add("🌐 Backend Bağlantısını Test Et", null, async (s, e) => await TestBackendConnection());
        actionsMenu.DropDownItems.Add("📋 Cihaz ID'yi Kopyala", null, (s, e) => CopyDeviceId());
        actionsMenu.DropDownItems.Add(new ToolStripSeparator());
        actionsMenu.DropDownItems.Add("⬆️ Güncelleme Kontrolü", null, async (s, e) => await CheckForUpdates());
        menu.Items.Add(actionsMenu);

        menu.Items.Add("📊 Durum Penceresi", null, (s, e) => ShowStatusWindow());
        menu.Items.Add("📁 Log Klasörünü Aç", null, (s, e) => OpenLogFolder());
        menu.Items.Add("ℹ️ Hakkında", null, (s, e) => ShowAbout());

        menu.Items.Add(new ToolStripSeparator());

        // Auto-start toggle
        var autoStartItem = new ToolStripMenuItem("🚀 Windows ile Başlat")
        {
            Name = "autoStartItem",
            Checked = IsAutoStartEnabled()
        };
        autoStartItem.Click += (s, e) => ToggleAutoStart(autoStartItem);
        menu.Items.Add(autoStartItem);

        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add("❌ Çıkış", null, (s, e) => ExitApplication());

        return menu;
    }

    private async Task CheckAgentStatusAsync()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", "MudoSoftAgentPipe", PipeDirection.InOut);
            
            try
            {
                await client.ConnectAsync(3000);
            }
            catch (TimeoutException)
            {
                UpdateStatus(AgentStatus.Disconnected, "Pipe timeout");
                return;
            }

            var request = JsonSerializer.Serialize(new { command = "status" });
            var requestBytes = Encoding.UTF8.GetBytes(request);
            await client.WriteAsync(requestBytes);
            await client.FlushAsync();

            var buffer = new byte[4096];
            var bytesRead = await client.ReadAsync(buffer);
            var response = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            
            var statusInfo = JsonSerializer.Deserialize<AgentStatusResponse>(response, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            
            if (statusInfo != null)
            {
                _agentVersion = statusInfo.Version ?? "?";
                _deviceId = statusInfo.DeviceId ?? "?";
                _lastHeartbeat = statusInfo.LastHeartbeat;
                
                // Update backend URL from Agent
                if (!string.IsNullOrEmpty(statusInfo.BackendUrl))
                {
                    _backendUrl = statusInfo.BackendUrl;
                }
                
                // NOTE: Remote Desktop is now handled by RDHelper (elevated process)
                // The following code is disabled - Agent's HelperLauncher manages RDHelper
                // if (!_rdServiceStarted && _deviceId != "?" && !string.IsNullOrEmpty(_backendUrl))
                // {
                //     _ = StartRemoteDesktopService();
                // }

                var timeSinceHeartbeat = DateTime.UtcNow - _lastHeartbeat;
                if (timeSinceHeartbeat.TotalSeconds < 60)
                {
                    UpdateStatus(AgentStatus.Connected, "Korumalı");
                }
                else if (timeSinceHeartbeat.TotalMinutes < 5)
                {
                    UpdateStatus(AgentStatus.Warning, "Son HB: " + FormatTimeAgo(_lastHeartbeat));
                }
                else
                {
                    UpdateStatus(AgentStatus.Disconnected, "Backend bağlantısı yok");
                }
            }
        }
        catch (Exception ex)
        {
            var errorMsg = ex.GetType().Name;
            if (errorMsg.Length > 20) errorMsg = errorMsg[..20];
            UpdateStatus(AgentStatus.Disconnected, errorMsg);
        }
    }

    private void UpdateStatus(AgentStatus status, string statusText)
    {
        _previousStatus = _currentStatus;
        _currentStatus = status;
        
        _trayIcon.Icon = CreateStatusIcon(status);
        _trayIcon.Text = $"Orchestra - {statusText}";

        // Update context menu
        if (_contextMenu.Items["statusItem"] is ToolStripMenuItem statusItem)
        {
            var icon = status switch
            {
                AgentStatus.Connected => "🟢",
                AgentStatus.Warning => "🟡",
                _ => "🔴"
            };
            statusItem.Text = $"{icon} Durum: {statusText}";
        }

        if (_contextMenu.Items["versionItem"] is ToolStripMenuItem versionItem)
        {
            versionItem.Text = $"📦 Versiyon: {_agentVersion}";
        }

        if (_contextMenu.Items["deviceItem"] is ToolStripMenuItem deviceItem)
        {
            var truncatedId = _deviceId.Length > 12 ? _deviceId[..12] + "..." : _deviceId;
            deviceItem.Text = $"💻 Cihaz: {truncatedId}";
        }

        if (_contextMenu.Items["heartbeatItem"] is ToolStripMenuItem heartbeatItem)
        {
            heartbeatItem.Text = $"⏱️ Son Bağlantı: {(_lastHeartbeat == DateTime.MinValue ? "-" : FormatTimeAgo(_lastHeartbeat))}";
        }

        // Show notification on status change (not on first check)
        if (!_firstCheck && _previousStatus != _currentStatus)
        {
            ShowStatusNotification(status, statusText);
        }
        _firstCheck = false;
    }

    private void ShowStatusNotification(AgentStatus status, string statusText)
    {
        var (title, icon) = status switch
        {
            AgentStatus.Connected => ("Bağlantı Kuruldu ✓", ToolTipIcon.Info),
            AgentStatus.Warning => ("Uyarı!", ToolTipIcon.Warning),
            _ => ("Bağlantı Kesildi!", ToolTipIcon.Error)
        };
        
        _trayIcon.ShowBalloonTip(3000, title, statusText, icon);
    }

    private Icon CreateStatusIcon(AgentStatus status)
    {
        var bitmap = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        var color = status switch
        {
            AgentStatus.Connected => Color.FromArgb(16, 185, 129),
            AgentStatus.Warning => Color.FromArgb(245, 158, 11),
            _ => Color.FromArgb(239, 68, 68)
        };

        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, 1, 1, 14, 14);

        using var font = new Font("Segoe UI", 7, FontStyle.Bold);
        using var textBrush = new SolidBrush(Color.White);
        var textSize = g.MeasureString("M", font);
        g.DrawString("M", font, textBrush, (16 - textSize.Width) / 2, (16 - textSize.Height) / 2);

        return Icon.FromHandle(bitmap.GetHicon());
    }

    private void ShowStatusWindow()
    {
        var form = new StatusForm(_currentStatus, _agentVersion, _deviceId, _lastHeartbeat);
        form.ShowDialog();
    }

    private async Task TestBackendConnection()
    {
        _trayIcon.ShowBalloonTip(2000, "Test", "Backend bağlantısı test ediliyor...", ToolTipIcon.Info);
        
        try
        {
            using var ping = new Ping();
            // This would need the actual backend IP/hostname
            var reply = await ping.SendPingAsync("10.0.102.60", 3000);
            
            if (reply.Status == IPStatus.Success)
            {
                _trayIcon.ShowBalloonTip(3000, "Başarılı ✓", $"Backend erişilebilir ({reply.RoundtripTime}ms)", ToolTipIcon.Info);
            }
            else
            {
                _trayIcon.ShowBalloonTip(3000, "Başarısız", $"Backend erişilemiyor: {reply.Status}", ToolTipIcon.Error);
            }
        }
        catch (Exception ex)
        {
            _trayIcon.ShowBalloonTip(3000, "Hata", ex.Message, ToolTipIcon.Error);
        }
    }

    private void RestartService()
    {
        var result = MessageBox.Show(
            "Agent servisini yeniden başlatmak istiyor musunuz?",
            "Servisi Yeniden Başlat",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result == DialogResult.Yes)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c net stop MudoSoftAgentNew && net start MudoSoftAgentNew",
                    Verb = "runas",
                    UseShellExecute = true,
                    CreateNoWindow = true
                };
                Process.Start(psi);
                _trayIcon.ShowBalloonTip(3000, "Servis", "Servis yeniden başlatılıyor...", ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Hata: {ex.Message}", "Servis Hatası", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private void CopyDeviceId()
    {
        if (!string.IsNullOrEmpty(_deviceId) && _deviceId != "?")
        {
            Clipboard.SetText(_deviceId);
            _trayIcon.ShowBalloonTip(2000, "Kopyalandı", "Cihaz ID panoya kopyalandı", ToolTipIcon.Info);
        }
    }

    private bool IsAutoStartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
            return key?.GetValue("MudoSoftTray") != null;
        }
        catch
        {
            return false;
        }
    }

    private void ToggleAutoStart(ToolStripMenuItem menuItem)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            if (key != null)
            {
                if (menuItem.Checked)
                {
                    key.DeleteValue("MudoSoftTray", false);
                    menuItem.Checked = false;
                    _trayIcon.ShowBalloonTip(2000, "Otomatik Başlatma", "Devre dışı bırakıldı", ToolTipIcon.Info);
                }
                else
                {
                    key.SetValue("MudoSoftTray", Application.ExecutablePath);
                    menuItem.Checked = true;
                    _trayIcon.ShowBalloonTip(2000, "Otomatik Başlatma", "Etkinleştirildi", ToolTipIcon.Info);
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Hata: {ex.Message}", "Registry Hatası", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowAbout()
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
        MessageBox.Show(
            $"Orchestra Endpoint Tray\n\nVersiyon: {version}\n\n© 2025 Orchestra\nTüm hakları saklıdır.",
            "Hakkında",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void OpenLogFolder()
    {
        var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MudoSoft", "Logs");
        if (!Directory.Exists(logPath))
            Directory.CreateDirectory(logPath);
        Process.Start("explorer.exe", logPath);
    }

    private void ExitApplication()
    {
        _trayIcon.Visible = false;
        _statusTimer.Stop();
        Application.Exit();
    }

    private string FormatTimeAgo(DateTime utcTime)
    {
        if (utcTime == DateTime.MinValue) return "-";
        var diff = DateTime.UtcNow - utcTime;
        if (diff.TotalSeconds < 60) return $"{(int)diff.TotalSeconds}sn önce";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}dk önce";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}sa önce";
        return $"{(int)diff.TotalDays}g önce";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _remoteDesktopService?.Dispose();
            _trayIcon.Dispose();
            _contextMenu.Dispose();
            _statusTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}

public enum AgentStatus
{
    Connected,
    Warning,
    Disconnected
}

public class AgentStatusResponse
{
    public string? Version { get; set; }
    public string? DeviceId { get; set; }
    public DateTime LastHeartbeat { get; set; }
    public bool IsConnected { get; set; }
    public string? BackendUrl { get; set; }
}

// Modern dark menu renderer
public class ModernMenuRenderer : ToolStripProfessionalRenderer
{
    public ModernMenuRenderer() : base(new ModernColorTable()) { }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected)
        {
            e.Graphics.FillRectangle(new SolidBrush(Color.FromArgb(30, 41, 59)), e.Item.ContentRectangle);
        }
        else
        {
            e.Graphics.FillRectangle(new SolidBrush(Color.FromArgb(51, 65, 85)), e.Item.ContentRectangle);
        }
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Color.FromArgb(226, 232, 240) : Color.FromArgb(100, 116, 139);
        base.OnRenderItemText(e);
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        e.Graphics.FillRectangle(new SolidBrush(Color.FromArgb(30, 41, 59)), e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        e.Graphics.DrawRectangle(new Pen(Color.FromArgb(51, 65, 85)), 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        var rect = e.Item.ContentRectangle;
        e.Graphics.DrawLine(new Pen(Color.FromArgb(51, 65, 85)), rect.Left, rect.Height / 2, rect.Right, rect.Height / 2);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = Color.FromArgb(148, 163, 184);
        base.OnRenderArrow(e);
    }
}

public class ModernColorTable : ProfessionalColorTable
{
    public override Color MenuBorder => Color.FromArgb(51, 65, 85);
    public override Color MenuItemBorder => Color.Transparent;
    public override Color MenuItemSelected => Color.FromArgb(51, 65, 85);
    public override Color MenuStripGradientBegin => Color.FromArgb(30, 41, 59);
    public override Color MenuStripGradientEnd => Color.FromArgb(30, 41, 59);
    public override Color ToolStripDropDownBackground => Color.FromArgb(30, 41, 59);
    public override Color ImageMarginGradientBegin => Color.FromArgb(30, 41, 59);
    public override Color ImageMarginGradientMiddle => Color.FromArgb(30, 41, 59);
    public override Color ImageMarginGradientEnd => Color.FromArgb(30, 41, 59);
}
