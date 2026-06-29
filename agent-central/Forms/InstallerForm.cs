using Orchestra.CentralAgent.Services;
using System.Drawing;
using System.Net.Http.Json;
using System.Runtime.Versioning;
using System.Windows.Forms;

namespace Orchestra.CentralAgent.Forms;

/// <summary>
/// İlk çalıştırmada gösterilen kurulum sihirbazı.
/// Adımları gösterip TightVNC + Windows Service kurulumunu yapar.
/// </summary>
[SupportedOSPlatform("windows")]
public class InstallerForm : Form
{
    private readonly CentralAgentConfig _config;
    private Panel _stepsPanel = null!;
    private Button _installBtn = null!;
    private Label _statusLabel = null!;

    private record StepItem(string Text, Label StepLabel, Label CheckLabel);
    private readonly List<StepItem> _steps = new();

    public InstallerForm(CentralAgentConfig config)
    {
        _config = config;
        Text            = "Orchestra Merkez Ajan — Kurulum";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        MinimizeBox     = false;
        StartPosition   = FormStartPosition.CenterScreen;
        Width           = 500;
        Height          = 450;
        BackColor       = Color.FromArgb(245, 247, 250);

        BuildLayout();
    }

    private void BuildLayout()
    {
        // Başlık
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 64,
            BackColor = Color.FromArgb(15, 90, 200)
        };
        header.Controls.Add(new Label
        {
            Text = "  Orchestra Merkez Ajan Kurulumu",
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 13f, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        });

        var subTitle = new Label
        {
            Text = "Bu uygulama teknisyenlerin yetkinizle ekranınızı uzaktan görüntülemesine\n" +
                   "olanak sağlar. Her bağlantıda onayınız istenir.",
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(60, 60, 80),
            Bounds = new Rectangle(20, 78, 460, 36),
            TextAlign = ContentAlignment.TopLeft
        };

        // Adımlar
        _stepsPanel = new Panel
        {
            Bounds = new Rectangle(20, 124, 460, 210),
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };

        string[] stepTexts = {
            "TightVNC sunucu kurulumu",
            "Windows servisi oluşturma",
            "Servis başlatma",
            "Backend'e kayıt",
            "Kullanıcı oturumu başlatılıyor"
        };

        for (int i = 0; i < stepTexts.Length; i++)
        {
            int y = 8 + i * 38;
            var chk = new Label
            {
                Text = "○",
                ForeColor = Color.Gray,
                Font = new Font("Segoe UI", 11f),
                Bounds = new Rectangle(12, y, 24, 28),
                TextAlign = ContentAlignment.MiddleCenter
            };
            var lbl = new Label
            {
                Text = stepTexts[i],
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = Color.FromArgb(60, 60, 80),
                Bounds = new Rectangle(42, y, 400, 28),
                TextAlign = ContentAlignment.MiddleLeft
            };
            _stepsPanel.Controls.AddRange(new Control[] { chk, lbl });
            _steps.Add(new StepItem(stepTexts[i], lbl, chk));
        }

        _statusLabel = new Label
        {
            Text = "Kuruluma başlamak için 'Kur' butonuna tıklayın.",
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Color.Gray,
            Bounds = new Rectangle(20, 345, 460, 22),
            TextAlign = ContentAlignment.MiddleLeft
        };

        _installBtn = new Button
        {
            Text = "  Kur",
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.FromArgb(15, 90, 200),
            FlatStyle = FlatStyle.Flat,
            Bounds = new Rectangle(20, 375, 200, 38),
            Cursor = Cursors.Hand
        };
        _installBtn.FlatAppearance.BorderSize = 0;
        _installBtn.Click += OnInstallClick;

        var cancelBtn = new Button
        {
            Text = "İptal",
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Color.FromArgb(80, 80, 100),
            BackColor = Color.FromArgb(225, 225, 230),
            FlatStyle = FlatStyle.Flat,
            Bounds = new Rectangle(240, 375, 120, 38),
            Cursor = Cursors.Hand
        };
        cancelBtn.FlatAppearance.BorderSize = 0;
        cancelBtn.Click += (_, _) => Close();

        Controls.AddRange(new Control[]
        {
            header, subTitle, _stepsPanel, _statusLabel, _installBtn, cancelBtn
        });
    }

    private async void OnInstallClick(object? sender, EventArgs e)
    {
        _installBtn.Enabled = false;
        _installBtn.Text    = "Kuruluyor...";

        await Task.Run(async () =>
        {
            // EXE'yi stabil konuma kopyala (Downloads'tan çalıştırma kırılganlığını önler).
            // Servis ve logon görevi hep bu sabit yolu kullanır.
            var sourceExe = Environment.ProcessPath!;
            var exePath   = EnsureInstalledToProgramFiles(sourceExe);
            var vncSetup = new VncSetupService(
                Microsoft.Extensions.Options.Options.Create(_config));

            // Adım 1 — TightVNC
            SetStep(0, "active");
            if (!vncSetup.IsVncInstalled())
            {
                var (ok, msg) = await vncSetup.InstallAsync();
                SetStep(0, ok ? "done" : "error", msg);
                if (!ok) { SetStatus($"Hata: {msg}"); return; }
            }
            else
            {
                // TightVNC kurulu — registry şifresini DPAPI dosyasıyla senkronize et
                var synced = vncSetup.SyncVncPassword();
                SetStep(0, "done", synced ? "Şifre senkronize edildi" : "Zaten kurulu");
            }

            // Adım 2 — Windows service
            SetStep(1, "active");
            var (svcOk, svcMsg) = InstallerService.RegisterService(_config.ServiceName, exePath);
            SetStep(1, svcOk ? "done" : "error", svcMsg);
            if (!svcOk) { SetStatus($"Hata: {svcMsg}"); return; }

            // Adım 3 — Servis başlat
            SetStep(2, "active");
            var (startOk, startMsg) = InstallerService.StartService(_config.ServiceName);
            SetStep(2, startOk ? "done" : "error", startMsg);
            if (!startOk) { SetStatus($"Hata: {startMsg}"); return; }

            // Adım 4 — VNC durumunu backend'e bildir
            SetStep(3, "active");
            var vncReported = await ReportVncStatusAsync(vncSetup);
            SetStep(3, vncReported ? "done" : "done",
                vncReported ? "VNC durumu bildirildi" : "Servis başladığında otomatik bildirilecek");

            // Adım 5 — Logon görevi + helper'ı şimdi başlat
            SetStep(4, "active");
            CreateHelperLogonTask(exePath);          // her oturum açılışında otomatik başlat
            var helperOk = StartHelperInUserSession(exePath); // şimdi de başlat
            SetStep(4, "done",
                helperOk ? "Otomatik başlatma kuruldu" : "Oturum açılışında başlatılacak");

            SetStatus("Kurulum tamamlandı! Bu pencere kapatılabilir.");
            Invoke(() =>
            {
                _installBtn.Text    = "✓ Tamamlandı";
                _installBtn.BackColor = Color.FromArgb(0, 140, 70);
            });
        });
    }

    private void SetStep(int index, string status, string? detail = null)
    {
        if (index >= _steps.Count) return;
        var step = _steps[index];
        Invoke(() =>
        {
            step.StepLabel.Text = detail != null
                ? $"{step.Text} — {detail}"
                : step.Text;

            switch (status)
            {
                case "active":
                    step.CheckLabel.Text      = "⏳";
                    step.CheckLabel.ForeColor = Color.FromArgb(15, 90, 200);
                    break;
                case "done":
                    step.CheckLabel.Text      = "✓";
                    step.CheckLabel.ForeColor = Color.FromArgb(0, 140, 70);
                    break;
                case "error":
                    step.CheckLabel.Text      = "✕";
                    step.CheckLabel.ForeColor = Color.Red;
                    break;
            }
        });
    }

    private async Task<bool> ReportVncStatusAsync(VncSetupService vncSetup)
    {
        try
        {
            var identity = new DeviceIdentityService(
                Microsoft.Extensions.Options.Options.Create(_config));
            var deviceId  = identity.GetDeviceId();
            bool installed = vncSetup.IsVncInstalled();
            // Şifreyi her zaman raporla — DB'de boş kalmasın (port erişilemese de zararsız)
            string? password = null;
            try { password = vncSetup.GetOrGeneratePassword(); } catch { }

            using var http = new HttpClient
            {
                BaseAddress = new Uri(_config.BackendUrl),
                Timeout = TimeSpan.FromSeconds(10)
            };
            var report = new { DeviceId = deviceId, Installed = installed, Password = password, Port = 5900 };
            var resp = await http.PostAsJsonAsync("/api/agent/vnc-status", report);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    /// <summary>
    /// EXE'yi sabit kuruluma kopyalar (C:\Program Files\Orchestra). Servis ve logon görevi
    /// bu yolu kullanır; böylece indirilen dosya silinse bile ajan çalışmaya devam eder.
    /// Kopyalanamazsa kaynak yoldan devam eder.
    /// </summary>
    private static string EnsureInstalledToProgramFiles(string sourceExe)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Orchestra");
            Directory.CreateDirectory(dir);
            var dest = Path.Combine(dir, "OrchestraCentralAgent.exe");

            if (!string.Equals(sourceExe, dest, StringComparison.OrdinalIgnoreCase))
                File.Copy(sourceExe, dest, overwrite: true);

            return dest;
        }
        catch { return sourceExe; }
    }

    /// <summary>
    /// Helper'ı her oturum açılışında başlatmak için HKLM\...\Run anahtarını yazar.
    /// Run anahtarı, oturum açan HER kullanıcının KENDİ interaktif oturumunda (kendi token'ıyla,
    /// UI gösterebilir şekilde) çalıştırır — scheduled task principal/session sorunlarını ortadan
    /// kaldırır. Mutex çift çalışmayı engeller. Eski scheduled task'lar temizlenir.
    /// </summary>
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string RunKeyName = "OrchestraCentralAgentHelper";

    private static void CreateHelperLogonTask(string exePath)
    {
        // Eski scheduled task'ları temizle (önceki sürümlerden — yanlış principal sorunu)
        RunProcess("schtasks.exe", "/delete /tn \"Orchestra Merkez Ajan Helper\" /f");
        RunProcess("schtasks.exe", "/delete /tn \"OrchestraCentralAgent\" /f");
        RunProcess("schtasks.exe", "/delete /tn \"OrchestraCentralAgentHelper\" /f");

        try
        {
            using var run = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(RunKeyPath, writable: true);
            run?.SetValue(RunKeyName, $"\"{exePath}\" --helper",
                Microsoft.Win32.RegistryValueKind.String);
        }
        catch { }
    }

    private static bool StartHelperInUserSession(string exePath)
    {
        try
        {
            // Sadece kendi kullanıcı oturumundaki helper'ları öldür.
            // Session 0'da çalışan Windows servisini (--service) kesinlikle öldürme!
            var currentSession = System.Diagnostics.Process.GetCurrentProcess().SessionId;
            foreach (var p in System.Diagnostics.Process.GetProcessesByName("OrchestraCentralAgent")
                .Where(p => p.Id != Environment.ProcessId && p.SessionId == currentSession))
            {
                try { p.Kill(); p.WaitForExit(2000); } catch { }
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exePath, "--helper")
            {
                UseShellExecute = false,
                CreateNoWindow  = true,
                WindowStyle     = System.Diagnostics.ProcessWindowStyle.Hidden
            });
            return true;
        }
        catch { return false; }
    }

    private static void RunProcess(string exe, string args)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(exe, args)
            {
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var p = System.Diagnostics.Process.Start(psi);
            p?.WaitForExit(10000);
        }
        catch { }
    }

    private void SetStatus(string msg) =>
        Invoke(() => _statusLabel.Text = msg);
}
