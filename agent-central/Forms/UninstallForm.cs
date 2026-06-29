using Orchestra.CentralAgent.Services;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.Versioning;
using System.Windows.Forms;

namespace Orchestra.CentralAgent.Forms;

/// <summary>
/// Orchestra Merkez Ajan + TightVNC + tüm verileri kaldırır.
/// </summary>
[SupportedOSPlatform("windows")]
public class UninstallForm : Form
{
    private readonly CentralAgentConfig _config;
    private Panel _stepsPanel = null!;
    private Button _uninstallBtn = null!;
    private Label _statusLabel = null!;

    private record StepItem(string Text, Label StepLabel, Label CheckLabel);
    private readonly List<StepItem> _steps = new();

    public UninstallForm(CentralAgentConfig config)
    {
        _config = config;
        Text            = "Orchestra Merkez Ajan — Kaldırma";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        MinimizeBox     = false;
        StartPosition   = FormStartPosition.CenterScreen;
        Width           = 500;
        Height          = 420;
        BackColor       = Color.FromArgb(245, 247, 250);
        BuildLayout();
    }

    private void BuildLayout()
    {
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 64,
            BackColor = Color.FromArgb(180, 30, 30)
        };
        header.Controls.Add(new Label
        {
            Text = "  Orchestra Merkez Ajan Kaldırma",
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 13f, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        });

        var subTitle = new Label
        {
            Text = "Bu işlem servis, TightVNC ve tüm Orchestra bileşenlerini bu bilgisayardan kaldırır.",
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(60, 60, 80),
            Bounds = new Rectangle(20, 78, 460, 36),
            TextAlign = ContentAlignment.TopLeft
        };

        _stepsPanel = new Panel
        {
            Bounds = new Rectangle(20, 124, 460, 180),
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };

        string[] stepTexts =
        {
            "Servis durduruluyor",
            "Servis kaldırılıyor",
            "TightVNC kaldırılıyor",
            "Güvenlik duvarı kuralı siliniyor",
            "Veriler ve klasörler temizleniyor"
        };

        for (int i = 0; i < stepTexts.Length; i++)
        {
            int y = 6 + i * 32;
            var chk = new Label
            {
                Text = "○",
                ForeColor = Color.Gray,
                Font = new Font("Segoe UI", 11f),
                Bounds = new Rectangle(12, y, 24, 26),
                TextAlign = ContentAlignment.MiddleCenter
            };
            var lbl = new Label
            {
                Text = stepTexts[i],
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = Color.FromArgb(60, 60, 80),
                Bounds = new Rectangle(42, y, 400, 26),
                TextAlign = ContentAlignment.MiddleLeft
            };
            _stepsPanel.Controls.AddRange(new Control[] { chk, lbl });
            _steps.Add(new StepItem(stepTexts[i], lbl, chk));
        }

        _statusLabel = new Label
        {
            Text = "Devam etmek için 'Kaldır' butonuna tıklayın.",
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Color.Gray,
            Bounds = new Rectangle(20, 315, 460, 22),
            TextAlign = ContentAlignment.MiddleLeft
        };

        _uninstallBtn = new Button
        {
            Text = "  Kaldır",
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.FromArgb(180, 30, 30),
            FlatStyle = FlatStyle.Flat,
            Bounds = new Rectangle(20, 345, 200, 38),
            Cursor = Cursors.Hand
        };
        _uninstallBtn.FlatAppearance.BorderSize = 0;
        _uninstallBtn.Click += OnUninstallClick;

        var cancelBtn = new Button
        {
            Text = "İptal",
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Color.FromArgb(80, 80, 100),
            BackColor = Color.FromArgb(225, 225, 230),
            FlatStyle = FlatStyle.Flat,
            Bounds = new Rectangle(240, 345, 120, 38),
            Cursor = Cursors.Hand
        };
        cancelBtn.FlatAppearance.BorderSize = 0;
        cancelBtn.Click += (_, _) => Close();

        Controls.AddRange(new Control[]
        {
            header, subTitle, _stepsPanel, _statusLabel, _uninstallBtn, cancelBtn
        });
    }

    private async void OnUninstallClick(object? sender, EventArgs e)
    {
        _uninstallBtn.Enabled = false;
        _uninstallBtn.Text = "Kaldırılıyor...";

        await Task.Run(() =>
        {
            // Adım 1 — Servisi durdur
            SetStep(0, "active");
            try
            {
                using var sc = new System.ServiceProcess.ServiceController(_config.ServiceName);
                if (sc.Status == System.ServiceProcess.ServiceControllerStatus.Running)
                {
                    sc.Stop();
                    sc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
                }
                SetStep(0, "done");
            }
            catch (Exception ex) { SetStep(0, "done", ex.Message); }

            // Adım 2 — Servisi sil + logon görevi + helper süreçleri
            SetStep(1, "active");
            RunProcess("sc.exe", $"delete \"{_config.ServiceName}\"");
            // Logon scheduled task'ları sil (eski + yeni adlar)
            RunProcess("schtasks.exe", "/delete /tn \"OrchestraCentralAgentHelper\" /f");
            RunProcess("schtasks.exe", "/delete /tn \"Orchestra Merkez Ajan Helper\" /f");
            RunProcess("schtasks.exe", "/delete /tn \"OrchestraCentralAgent\" /f");
            // HKLM Run anahtarı (helper otomatik başlatma)
            try
            {
                using var run = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true);
                run?.DeleteValue("OrchestraCentralAgentHelper", throwOnMissingValue: false);
            }
            catch { }
            // Çalışan helper süreçlerini kapat (kendimiz hariç)
            try
            {
                foreach (var p in Process.GetProcessesByName("OrchestraCentralAgent")
                    .Where(p => p.Id != Environment.ProcessId))
                { try { p.Kill(); p.WaitForExit(2000); } catch { } }
            }
            catch { }
            System.Threading.Thread.Sleep(500);
            SetStep(1, "done");

            // Adım 3 — TightVNC servisini durdur + kaldır
            SetStep(2, "active");
            try
            {
                using var sc = new System.ServiceProcess.ServiceController("tvnserver");
                if (sc.Status == System.ServiceProcess.ServiceControllerStatus.Running)
                {
                    sc.Stop();
                    sc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
                }
            }
            catch { }
            RunProcess("sc.exe", "delete tvnserver");
            // MSI uninstall via product GUID
            RunProcess("msiexec.exe", "/x {8B7C587F-9B9E-4C3B-9B1F-A9E7B9B9B9B9} /quiet /norestart");
            // Fallback — wmic uninstall by name
            RunProcess("wmic.exe", "product where \"Name like 'TightVNC%'\" call uninstall /nointeractive");
            SetStep(2, "done");

            // Adım 4 — Güvenlik duvarı kuralı
            SetStep(3, "active");
            RunProcess("netsh.exe", "advfirewall firewall delete rule name=\"Orchestra VNC\"");
            SetStep(3, "done");

            // Adım 5 — Dosya ve klasörleri temizle
            SetStep(4, "active");
            try
            {
                var dataDir = @"C:\ProgramData\OrchestraCentralAgent";
                if (Directory.Exists(dataDir))
                    Directory.Delete(dataDir, recursive: true);
            }
            catch { }
            // Temp MSI dosyası
            try
            {
                var msi = Path.Combine(Path.GetTempPath(), "tightvnc_orchestra.msi");
                if (File.Exists(msi)) File.Delete(msi);
            }
            catch { }
            // Program Files kurulum klasörü (sabit konum)
            try
            {
                var installDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Orchestra");
                if (Directory.Exists(installDir))
                    Directory.Delete(installDir, recursive: true);
            }
            catch { }
            SetStep(4, "done");

            SetStatus("Kaldırma işlemi tamamlandı. Bu pencereyi kapatabilirsiniz.");
            Invoke(() =>
            {
                _uninstallBtn.Text = "✓ Tamamlandı";
                _uninstallBtn.BackColor = Color.FromArgb(0, 140, 70);
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
                    step.CheckLabel.Text = "⏳";
                    step.CheckLabel.ForeColor = Color.FromArgb(15, 90, 200);
                    break;
                case "done":
                    step.CheckLabel.Text = "✓";
                    step.CheckLabel.ForeColor = Color.FromArgb(0, 140, 70);
                    break;
                case "error":
                    step.CheckLabel.Text = "✕";
                    step.CheckLabel.ForeColor = Color.Red;
                    break;
            }
        });
    }

    private void SetStatus(string msg) =>
        Invoke(() => _statusLabel.Text = msg);

    private static void RunProcess(string exe, string args)
    {
        try
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(30000);
        }
        catch { }
    }
}
