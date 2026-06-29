using Orchestra.CentralAgent.Forms;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.ServiceProcess;
using System.Windows.Forms;

namespace Orchestra.CentralAgent.Services;

/// <summary>
/// İlk çalıştırmada kurulum ekranını gösterir ve şu adımları gerçekleştirir:
///   1. TightVNC kurulumu
///   2. Windows service kaydı (bu exe --service argümanıyla)
///   3. Servisi başlatma
///   4. Backend'e cihazı kaydetme (heartbeat ilk çalıştığında kendi kaydeder)
/// </summary>
[SupportedOSPlatform("windows")]
public static class InstallerService
{
    public static async Task RunAsync(CentralAgentConfig config)
    {
        if (IsServiceInstalled(config.ServiceName))
        {
            var answer = MessageBox.Show(
                "Orchestra Merkez Ajanı bu bilgisayara zaten kurulu.\n\n" +
                "• Evet      → Güncelleme / onarım yap\n" +
                "• Hayır     → Tüm Orchestra bileşenlerini kaldır\n" +
                "• İptal     → Çıkış",
                "Orchestra Merkez Ajan",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);

            if (answer == DialogResult.Cancel) return;

            if (answer == DialogResult.No)
            {
                var uninstall = new Orchestra.CentralAgent.Forms.UninstallForm(config);
                Application.Run(uninstall);
                return;
            }

            // Evet = Güncelleme/Onarım
            StopService(config.ServiceName);
        }

        var form = new InstallerForm(config);
        Application.Run(form);
    }

    public static void StopService(string serviceName)
    {
        try
        {
            using var sc = new ServiceController(serviceName);
            if (sc.Status == ServiceControllerStatus.Running)
            {
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
            }
        }
        catch { }
    }

    public static bool IsServiceInstalled(string serviceName)
    {
        try
        {
            using var sc = new ServiceController(serviceName);
            _ = sc.Status;
            return true;
        }
        catch { return false; }
    }

    public static bool IsServiceRunning(string serviceName)
    {
        try
        {
            using var sc = new ServiceController(serviceName);
            return sc.Status == ServiceControllerStatus.Running;
        }
        catch { return false; }
    }

    /// <summary>
    /// sc.exe ile Windows servisini kaydeder.
    /// </summary>
    public static (bool ok, string msg) RegisterService(string serviceName, string exePath)
    {
        try
        {
            // Önce varsa sil
            RunProcess("sc.exe", $"delete \"{serviceName}\"");
            System.Threading.Thread.Sleep(1000);

            var result = RunProcess("sc.exe",
                $"create \"{serviceName}\" binPath= \"\\\"{exePath}\\\" --service\" " +
                $"start= auto DisplayName= \"Orchestra Merkez Ajan\"");

            if (!result.ok) return result;

            RunProcess("sc.exe", $"description \"{serviceName}\" \"Orchestra Merkez Calisani Uzak Destek Ajani\"");
            return (true, "Servis kaydedildi");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public static (bool ok, string msg) StartService(string serviceName)
    {
        try
        {
            using var sc = new ServiceController(serviceName);
            sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
            return (true, "Servis başlatıldı");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static (bool ok, string msg) RunProcess(string exe, string args)
    {
        try
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                CreateNoWindow  = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true
            };
            using var p = Process.Start(psi)!;
            p.WaitForExit(10000);
            var err = p.StandardError.ReadToEnd().Trim();
            return (p.ExitCode == 0, err.Length > 0 ? err : "OK");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
