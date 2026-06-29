using System.Collections.Concurrent;

namespace Orchestra.Backend.Services;

/// <summary>
/// Merkez cihazlarında bağlantı öncesi kullanıcı onayını yönetir.
/// Her istek 60 saniye bekler; kullanıcı onaylarsa Approved, reddederse Denied, süre aşılırsa Timeout olur.
/// </summary>
public class ConsentRequestManager
{
    private readonly ConcurrentDictionary<string, ConsentEntry> _requests = new();

    public string CreateRequest(string deviceId, string requesterName, string requesterUsername)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var entry = new ConsentEntry
        {
            RequestId     = requestId,
            DeviceId      = deviceId,
            RequesterName = requesterName,
            RequesterUsername = requesterUsername,
            CreatedAt     = DateTime.UtcNow,
            Tcs           = new TaskCompletionSource<bool>(),
            Cts           = cts
        };

        // 60 sn sonra otomatik Timeout
        cts.Token.Register(() =>
        {
            if (entry.Status == ConsentStatus.Pending)
            {
                entry.Status = ConsentStatus.Timeout;
                entry.Tcs.TrySetResult(false);
            }
        });

        _requests[requestId] = entry;
        return requestId;
    }

    public ConsentEntry? Get(string requestId) =>
        _requests.TryGetValue(requestId, out var e) ? e : null;

    public ConsentStatusDto GetStatus(string requestId)
    {
        if (!_requests.TryGetValue(requestId, out var entry))
            return new ConsentStatusDto { Status = "not_found" };

        return new ConsentStatusDto
        {
            Status = entry.Status switch
            {
                ConsentStatus.Pending  => "pending",
                ConsentStatus.Approved => "approved",
                ConsentStatus.Denied   => "denied",
                ConsentStatus.Timeout  => "timeout",
                _                      => "unknown"
            },
            DeviceId          = entry.DeviceId,
            RequesterName     = entry.RequesterName,
            RequesterUsername = entry.RequesterUsername,
            DenyReason        = entry.DenyReason
        };
    }

    public void Resolve(string requestId, bool approved, string? denyReason = null)
    {
        if (!_requests.TryGetValue(requestId, out var entry)) return;
        if (entry.Status != ConsentStatus.Pending) return;

        entry.Status = approved ? ConsentStatus.Approved : ConsentStatus.Denied;
        if (!approved && denyReason != null) entry.DenyReason = denyReason;
        entry.Tcs.TrySetResult(approved);
        entry.Cts.Dispose();
    }

    /// <summary>
    /// Belirli bir deviceId için bekleyen onay isteği varsa iptal eder (cihaz çevrimdışı olunca vs.)
    /// </summary>
    public void CancelPending(string deviceId)
    {
        foreach (var kvp in _requests)
        {
            if (kvp.Value.DeviceId == deviceId && kvp.Value.Status == ConsentStatus.Pending)
                Resolve(kvp.Key, false);
        }
    }

    public void Remove(string requestId) => _requests.TryRemove(requestId, out _);
}

public class ConsentEntry
{
    public string RequestId       { get; set; } = "";
    public string DeviceId        { get; set; } = "";
    public string RequesterName   { get; set; } = "";
    public string RequesterUsername { get; set; } = "";
    public DateTime CreatedAt     { get; set; }
    public ConsentStatus Status   { get; set; } = ConsentStatus.Pending;
    public string? DenyReason     { get; set; }
    public TaskCompletionSource<bool> Tcs { get; set; } = null!;
    public CancellationTokenSource Cts  { get; set; } = null!;
}

public enum ConsentStatus { Pending, Approved, Denied, Timeout }

public class ConsentStatusDto
{
    public string Status          { get; set; } = "";
    public string? DeviceId       { get; set; }
    public string? RequesterName  { get; set; }
    public string? RequesterUsername { get; set; }
    public string? DenyReason     { get; set; }
}
