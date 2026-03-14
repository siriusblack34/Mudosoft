using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mudosoft.Agent.Interfaces;
using Mudosoft.Agent.Models;
using Mudosoft.Shared.Dtos;
using System.Net.Http.Json;

namespace Mudosoft.Agent.Services.Collectors;

/// <summary>
/// Collector sonuçlarını toplu olarak backend'e POST eder.
/// </summary>
public sealed class CollectorReportSender
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly AgentConfig _config;
    private readonly IDeviceIdentityProvider _identity;
    private readonly ILogger<CollectorReportSender> _logger;

    public CollectorReportSender(
        IHttpClientFactory httpFactory,
        IOptions<AgentConfig> config,
        IDeviceIdentityProvider identity,
        ILogger<CollectorReportSender> logger)
    {
        _httpFactory = httpFactory;
        _config = config.Value;
        _identity = identity;
        _logger = logger;
    }

    public async Task SendAsync(IReadOnlyList<CollectorResult> results, CancellationToken ct)
    {
        if (results.Count == 0) return;

        var report = new CollectorReportDto
        {
            DeviceId = _identity.GetDeviceId(),
            TimestampUtc = DateTime.UtcNow,
            Results = results.Select(r => new CollectorResultDto
            {
                CollectorName = r.CollectorName,
                TimestampUtc = r.TimestampUtc,
                Severity = r.Severity,
                JsonData = r.JsonData,
                Success = r.Success,
                ErrorMessage = r.ErrorMessage
            }).ToList()
        };

        try
        {
            var client = _httpFactory.CreateClient();
            var url = $"{_config.BackendUrl.TrimEnd('/')}/api/agent/collector-report";
            var response = await client.PostAsJsonAsync(url, report, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Collector report failed: {Status}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Collector report send error");
        }
    }
}
