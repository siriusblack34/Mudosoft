using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mudosoft.Agent.Interfaces;
using Mudosoft.Agent.Models;
using Mudosoft.Shared.Dtos;
using System.Text.Json;

namespace Mudosoft.Agent.Services.Collectors;

/// <summary>
/// Belirtilen klasörlerdeki eski dosyaları periyodik olarak temizler.
/// %TEMP%, Windows\Temp, CBS logları gibi hedefler yapılandırılabilir.
/// Her çalışmada silinen dosya sayısı ve kazanılan alan raporlanır.
/// </summary>
public sealed class ScheduledCleanupCollector : ICollector
{
    private readonly ScheduledCleanupConfig _config;
    private readonly ILogger<ScheduledCleanupCollector> _logger;

    public string Name => "ScheduledCleanup";
    public TimeSpan Interval => TimeSpan.FromSeconds(_config.IntervalSeconds);
    public bool Enabled => _config.Enabled;

    public ScheduledCleanupCollector(
        IOptions<CollectorsConfig> config,
        ILogger<ScheduledCleanupCollector> logger)
    {
        _config = config.Value.ScheduledCleanup;
        _logger = logger;
    }

    public Task<CollectorResult> CollectAsync(CancellationToken ct)
    {
        var results = new List<CleanupResultDto>();
        double totalFreedMB = 0;

        foreach (var target in _config.Targets)
        {
            if (ct.IsCancellationRequested) break;

            var expandedPath = Environment.ExpandEnvironmentVariables(target.Path);
            var dto = CleanDirectory(expandedPath, target.MaxAgeDays);
            results.Add(dto);
            totalFreedMB += dto.FreedMB;
        }

        if (totalFreedMB > 0)
        {
            _logger.LogInformation("Cleanup completed: {MB:F1} MB freed across {Count} targets",
                totalFreedMB, results.Count);
        }

        return Task.FromResult(new CollectorResult
        {
            CollectorName = Name,
            Success = true,
            Severity = "Info",
            JsonData = JsonSerializer.Serialize(results)
        });
    }

    private CleanupResultDto CleanDirectory(string path, int maxAgeDays)
    {
        var dto = new CleanupResultDto { TargetPath = path };

        if (!Directory.Exists(path))
        {
            dto.Error = "Directory not found";
            return dto;
        }

        var cutoff = DateTime.UtcNow.AddDays(-maxAgeDays);
        long totalFreed = 0;
        int deleted = 0;
        int folderDeleted = 0;

        try
        {
            // Dosyaları temizle
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var info = new FileInfo(file);
                    if (info.LastWriteTimeUtc < cutoff)
                    {
                        var size = info.Length;
                        info.Delete();
                        totalFreed += size;
                        deleted++;
                    }
                }
                catch (Exception ex)
                {
                    // Kilitli veya erişilemeyen dosyaları kaydet
                    dto.Errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
                }
            }

            // Alt klasörlerdeki eski dosyaları da temizle (1 seviye)
            foreach (var dir in Directory.EnumerateDirectories(path))
            {
                try
                {
                    var dirInfo = new DirectoryInfo(dir);
                    if (dirInfo.LastWriteTimeUtc < cutoff)
                    {
                        // Boş veya eski klasörleri sil
                        var files = dirInfo.GetFiles("*", SearchOption.AllDirectories);
                        if (files.All(f => f.LastWriteTimeUtc < cutoff))
                        {
                            try
                            {
                                var dirSize = files.Sum(f => f.Length);
                                dirInfo.Delete(true);
                                totalFreed += dirSize;
                                deleted += files.Length;
                                folderDeleted++;
                            }
                            catch (Exception ex)
                            {
                                dto.Errors.Add($"{dirInfo.Name}: {ex.Message}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    dto.Errors.Add($"{Path.GetFileName(dir)}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            dto.Error = ex.Message;
            _logger.LogWarning(ex, "Cleanup error for {Path}", path);
        }

        dto.FilesDeleted = deleted;
        dto.FolderDeleted = folderDeleted;
        dto.FreedMB = Math.Round(totalFreed / (1024.0 * 1024.0), 2);
        return dto;
    }
}
