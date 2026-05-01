using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;

namespace Orchestra.Tray;

/// <summary>
/// Handles Agent and Tray updates
/// Downloads from backend, stops service, replaces files, restarts
/// </summary>
public class UpdateService
{
    private readonly string _backendUrl;
    private readonly HttpClient _httpClient;
    
    public event Action<string>? OnLog;
    public event Action<int>? OnProgress;
    public event Action<bool, string>? OnComplete;

    public UpdateService(string backendUrl)
    {
        _backendUrl = backendUrl.TrimEnd('/');
        _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
    }

    public async Task<(bool hasUpdate, string? version)> CheckForUpdateAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_backendUrl}/api/updates/latest");
            if (!response.IsSuccessStatusCode)
            {
                Log($"Update check failed: {response.StatusCode}");
                return (false, null);
            }
            
            var json = await response.Content.ReadAsStringAsync();
            var doc = System.Text.Json.JsonDocument.Parse(json);
            
            if (doc.RootElement.TryGetProperty("version", out var versionProp))
            {
                var latestVersion = versionProp.GetString();
                Log($"Latest version: {latestVersion}");
                return (true, latestVersion);
            }
            
            return (false, null);
        }
        catch (Exception ex)
        {
            Log($"Update check error: {ex.Message}");
            return (false, null);
        }
    }

    public async Task PerformUpdateAsync()
    {
        try
        {
            Log("🔄 Güncelleme başlatılıyor...");
            OnProgress?.Invoke(5);
            
            // 1. Get latest version info
            var response = await _httpClient.GetAsync($"{_backendUrl}/api/updates/latest");
            if (!response.IsSuccessStatusCode)
            {
                OnComplete?.Invoke(false, $"Versiyon bilgisi alınamadı: {response.StatusCode}");
                return;
            }
            
            var json = await response.Content.ReadAsStringAsync();
            var doc = System.Text.Json.JsonDocument.Parse(json);
            
            if (!doc.RootElement.TryGetProperty("downloadUrl", out var urlProp))
            {
                OnComplete?.Invoke(false, "İndirme URL'si bulunamadı");
                return;
            }
            
            var downloadUrl = urlProp.GetString();
            if (string.IsNullOrEmpty(downloadUrl))
            {
                OnComplete?.Invoke(false, "Geçersiz indirme URL'si");
                return;
            }
            
            // Make relative URL absolute
            if (!downloadUrl.StartsWith("http"))
            {
                downloadUrl = $"{_backendUrl}{downloadUrl}";
            }
            
            Log($"📥 İndiriliyor: {downloadUrl}");
            OnProgress?.Invoke(15);
            
            // 2. Download update package
            var tempDir = Path.Combine(Path.GetTempPath(), $"MudoSoftUpdate_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            
            var zipPath = Path.Combine(tempDir, "update.zip");
            var extractDir = Path.Combine(tempDir, "extracted");
            
            var downloadResponse = await _httpClient.GetAsync(downloadUrl);
            if (!downloadResponse.IsSuccessStatusCode)
            {
                OnComplete?.Invoke(false, $"İndirme başarısız: {downloadResponse.StatusCode}");
                return;
            }
            
            using (var fs = new FileStream(zipPath, FileMode.Create))
            {
                await downloadResponse.Content.CopyToAsync(fs);
            }
            
            Log("✅ İndirme tamamlandı, çıkartılıyor...");
            OnProgress?.Invoke(50);
            
            // 3. Extract
            ZipFile.ExtractToDirectory(zipPath, extractDir, true);
            OnProgress?.Invoke(60);
            
            // 4. Prepare update script
            var serviceName = "MudosoftAgentService";
            var agentDir = @"C:\Program Files\MudoSoft\Agent";
            var trayExePath = Application.ExecutablePath;
            
            // PowerShell script for update
            var psScriptPath = Path.Combine(tempDir, "updater.ps1");
            var psContent = $@"
# MudoSoft Updater Script
Start-Sleep -Seconds 3

# 1. Stop Agent Service
Write-Host 'Stopping Agent service...'
Stop-Service -Name '{serviceName}' -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

# 2. Kill Tray process
Write-Host 'Stopping Tray...'
Get-Process -Name 'MudoSoft.Tray' -ErrorAction SilentlyContinue | Stop-Process -Force

Start-Sleep -Seconds 2

# 3. Copy new files
Write-Host 'Copying new files...'
if (Test-Path '{extractDir}\MudoSoft.Agent.exe') {{
    Copy-Item -Path '{extractDir}\MudoSoft.Agent.exe' -Destination '{agentDir}\' -Force
}}
if (Test-Path '{extractDir}\MudoSoft.Tray.exe') {{
    Copy-Item -Path '{extractDir}\MudoSoft.Tray.exe' -Destination '{agentDir}\' -Force
}}
Copy-Item -Path '{extractDir}\*.dll' -Destination '{agentDir}\' -Force -ErrorAction SilentlyContinue
Copy-Item -Path '{extractDir}\*.json' -Destination '{agentDir}\' -Force -ErrorAction SilentlyContinue

# 4. Start Agent Service
Write-Host 'Starting Agent service...'
Start-Service -Name '{serviceName}'

# 5. Start Tray
Write-Host 'Starting Tray...'
Start-Process -FilePath '{agentDir}\MudoSoft.Tray.exe'

# 6. Cleanup
Start-Sleep -Seconds 5
Remove-Item -Path '{tempDir}' -Recurse -Force -ErrorAction SilentlyContinue

Write-Host 'Update complete!'
";
            File.WriteAllText(psScriptPath, psContent);
            
            Log("🚀 Güncelleme uygulanıyor...");
            OnProgress?.Invoke(80);
            
            // 5. Run update script (will kill this process)
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-ExecutionPolicy Bypass -File \"{psScriptPath}\"",
                UseShellExecute = true,
                Verb = "runas", // Run as admin
                CreateNoWindow = false // Show window for debugging
            };
            
            try
            {
                Process.Start(psi);
                OnProgress?.Invoke(100);
                OnComplete?.Invoke(true, "Güncelleme başlatıldı, uygulama yeniden başlayacak...");
                
                // Exit Tray so update can proceed
                await Task.Delay(1000);
                Application.Exit();
            }
            catch (System.ComponentModel.Win32Exception)
            {
                OnComplete?.Invoke(false, "Yönetici yetkisi reddedildi");
            }
        }
        catch (Exception ex)
        {
            Log($"❌ Güncelleme hatası: {ex.Message}");
            OnComplete?.Invoke(false, $"Hata: {ex.Message}");
        }
    }

    private void Log(string message)
    {
        OnLog?.Invoke(message);
        Debug.WriteLine($"[Update] {message}");
    }
}
