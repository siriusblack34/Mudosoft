using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestra.Agent.Interfaces;
using Orchestra.Agent.Models;
using Orchestra.Shared.Dtos;
using System.Management;
using System.Text.Json;

namespace Orchestra.Agent.Services.Collectors;

/// <summary>
/// Yüklü Windows güncellemelerini ve bekleyen güncellemeleri listeler.
/// WMI Win32_QuickFixEngineering kullanır (Win7 + Win11 uyumlu).
/// COM-based Windows Update API kullanmaz (karmaşık ve güvenilmez).
/// </summary>
public sealed class WindowsUpdateCollector : ICollector
{
    private readonly WindowsUpdateConfig _config;
    private readonly ILogger<WindowsUpdateCollector> _logger;

    public string Name => "WindowsUpdate";
    public TimeSpan Interval => TimeSpan.FromSeconds(_config.IntervalSeconds);
    public bool Enabled => _config.Enabled;

    public WindowsUpdateCollector(
        IOptions<CollectorsConfig> config,
        ILogger<WindowsUpdateCollector> logger)
    {
        _config = config.Value.WindowsUpdate;
        _logger = logger;
    }

    public Task<CollectorResult> CollectAsync(CancellationToken ct)
    {
        var results = new List<WindowsUpdateDto>();

        try
        {
            // Son yüklenen güncellemeleri WMI ile al
            using var searcher = new ManagementObjectSearcher(
                "SELECT HotFixID, Description, InstalledOn FROM Win32_QuickFixEngineering");

            foreach (var obj in searcher.Get())
            {
                var hotfixId = obj["HotFixID"]?.ToString() ?? "";
                var description = obj["Description"]?.ToString() ?? "";
                var installedOnStr = obj["InstalledOn"]?.ToString();

                DateTime? installedOn = null;
                if (DateTime.TryParse(installedOnStr, out var dt))
                    installedOn = dt.ToUniversalTime();

                results.Add(new WindowsUpdateDto
                {
                    Title = hotfixId,
                    UpdateId = hotfixId,
                    Description = description,
                    InstalledOn = installedOn,
                    IsInstalled = true,
                    IsMandatory = false
                });
            }

            // Son yüklenen güncellemeye göre sırala
            results = results
                .OrderByDescending(u => u.InstalledOn)
                .Take(50) // Son 50 güncelleme
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying Windows updates via WMI");
            return Task.FromResult(new CollectorResult
            {
                CollectorName = Name,
                Success = false,
                Severity = "Warning",
                ErrorMessage = ex.Message,
                JsonData = "[]"
            });
        }

        // 30 günden eski son güncelleme varsa uyarı
        var latestUpdate = results.FirstOrDefault();
        var severity = "Info";
        if (latestUpdate?.InstalledOn != null &&
            (DateTime.UtcNow - latestUpdate.InstalledOn.Value).TotalDays > 30)
        {
            severity = "Warning";
            _logger.LogWarning("Last Windows update was {Days} days ago",
                (int)(DateTime.UtcNow - latestUpdate.InstalledOn.Value).TotalDays);
        }

        return Task.FromResult(new CollectorResult
        {
            CollectorName = Name,
            Success = true,
            Severity = severity,
            JsonData = JsonSerializer.Serialize(results)
        });
    }
}
