using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Orchestra.Backend.Data;
using Orchestra.Backend.Models;

namespace Orchestra.Backend.Services;

/// <summary>
/// DC Security event log'unu (Event ID 4624 interactive logon) tarayarak
/// PendingUserInstalls listesindeki kullanıcılar bir makineye login olduğunda
/// agent install'ı tetikleyen background worker.
///
/// AD-native akış: kullanıcı seçilir → kullanıcı herhangi bir domain PC'sine login olunca
/// Orchestra otomatik o IP'ye remote-install başlatır.
/// </summary>
public class UserInstallWatcherService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<UserInstallWatcherService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);
    // DC'de 4768 (Kerberos TGT issued) eventi watch ediyoruz — workstation'a interactive
    // login olduğunda DC'den TGT istenir; ClientAddress alanı workstation IP'sini taşır.
    // 4624 (Interactive Logon) DC'ye değil workstation'a düşer; o yüzden 4768 doğru olan.
    private const int KerberosAsEventId = 4768;

    public UserInstallWatcherService(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<UserInstallWatcherService> logger,
        IHttpClientFactory httpClientFactory)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Sadece Windows'ta event log polling yapabiliriz (Get-WinEvent gerek)
        if (!OperatingSystem.IsWindows())
        {
            _logger.LogWarning("[UserInstallWatcher] Non-Windows OS detected; service disabled.");
            return;
        }

        _logger.LogInformation("[UserInstallWatcher] Started; poll interval={Interval}s", PollInterval.TotalSeconds);

        // İlk başlatmada DC isimlerini öğren
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[UserInstallWatcher] Tick failed");
            }

            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();

        // 1) Expire eski waiting kayıtları
        var nowUtc = DateTime.UtcNow;
        var expired = await db.PendingUserInstalls
            .Where(p => p.Status == PendingUserInstallStatus.Waiting && p.ExpiresAt < nowUtc)
            .ToListAsync(ct);
        if (expired.Count > 0)
        {
            foreach (var p in expired)
            {
                p.Status = PendingUserInstallStatus.Expired;
                p.UpdatedAt = nowUtc;
            }
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("[UserInstallWatcher] {Count} pending install(s) expired", expired.Count);
        }

        // 2) Aktif (Waiting) kullanıcı listesini çek — yoksa DC'ye gitme
        var watching = await db.PendingUserInstalls
            .Where(p => p.Status == PendingUserInstallStatus.Waiting)
            .Select(p => p.SamAccountName.ToLower())
            .Distinct()
            .ToListAsync(ct);
        if (watching.Count == 0) return;

        var watchSet = new HashSet<string>(watching, StringComparer.OrdinalIgnoreCase);

        // 3) DC listesi (config + auto-discover)
        var dcs = ResolveDomainControllers();
        if (dcs.Count == 0)
        {
            _logger.LogWarning("[UserInstallWatcher] No domain controllers configured/discovered; skipping tick");
            return;
        }

        // 4) Her DC'yi paralel sorgula
        var tasks = dcs.Select(dc => QueryDcAsync(dc, watchSet, ct));
        var allEvents = (await Task.WhenAll(tasks)).SelectMany(x => x).ToList();
        if (allEvents.Count == 0) return;

        // Aynı kullanıcı birden çok DC'de logon eventi düşürebilir; sam başına ilk match'i al
        var firstByUser = allEvents
            .OrderBy(e => e.TimeCreated)
            .GroupBy(e => e.TargetUser, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        // 5) Pending kayıtlarını match'le ve install tetikle
        var pending = await db.PendingUserInstalls
            .Where(p => p.Status == PendingUserInstallStatus.Waiting)
            .ToListAsync(ct);

        foreach (var p in pending)
        {
            if (!firstByUser.TryGetValue(p.SamAccountName, out var ev)) continue;
            if (string.IsNullOrWhiteSpace(ev.IpAddress)) continue; // 4768'de IP olmadan match olmaz

            // Workstation hostname için reverse-DNS dene; başarısızsa IP göster
            string? hostname = TryReverseDns(ev.IpAddress);

            p.MatchedComputer = hostname ?? ev.IpAddress;
            p.MatchedIp = ev.IpAddress;
            p.MatchedAt = nowUtc;
            p.Status = PendingUserInstallStatus.Matched;
            p.UpdatedAt = nowUtc;
            await db.SaveChangesAsync(ct);

            _logger.LogInformation("[UserInstallWatcher] Matched {User} → {Pc} ({Ip})",
                p.SamAccountName, p.MatchedComputer, p.MatchedIp);

            try
            {
                var installId = await TriggerInstallByIpAsync(ev.IpAddress, ct);
                p.InstallId = installId;
                p.Status = PendingUserInstallStatus.Installing;
                p.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                _logger.LogInformation("[UserInstallWatcher] Install triggered for {User}: id={Id}", p.SamAccountName, installId);
            }
            catch (Exception ex)
            {
                p.Status = PendingUserInstallStatus.Failed;
                p.LastError = ex.Message;
                p.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                _logger.LogError(ex, "[UserInstallWatcher] Install trigger failed for {User}", p.SamAccountName);
            }
        }

        // 6) Installing → done/failed senkronize et (status endpoint'inden poll)
        await SyncInstallingStatusesAsync(db, ct);
    }

    private async Task SyncInstallingStatusesAsync(OrchestraDbContext db, CancellationToken ct)
    {
        var installing = await db.PendingUserInstalls
            .Where(p => p.Status == PendingUserInstallStatus.Installing && p.MatchedIp != null)
            .ToListAsync(ct);
        if (installing.Count == 0) return;

        var httpClient = _httpClientFactory.CreateClient("internal");
        httpClient.BaseAddress ??= new Uri(GetInternalBaseUrl());
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateSystemToken());

        foreach (var p in installing)
        {
            try
            {
                var resp = await httpClient.GetAsync($"/api/agent/remote-install/status?ip={Uri.EscapeDataString(p.MatchedIp!)}", ct);
                if (!resp.IsSuccessStatusCode) continue;
                var json = await resp.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                var phase = doc.RootElement.TryGetProperty("phase", out var phEl) ? phEl.GetString() : null;
                if (phase == "done" || phase == "warn")
                {
                    p.Status = PendingUserInstallStatus.Done;
                    p.UpdatedAt = DateTime.UtcNow;
                }
                else if (phase == "error")
                {
                    p.Status = PendingUserInstallStatus.Failed;
                    p.LastError = doc.RootElement.TryGetProperty("error", out var err) ? err.GetString() : "install failed";
                    p.UpdatedAt = DateTime.UtcNow;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[UserInstallWatcher] Status poll failed for {Ip}", p.MatchedIp);
            }
        }
        await db.SaveChangesAsync(ct);
    }

    private string DeriveAdDomain()
    {
        var sb = _config["Ldap:SearchBase"] ?? "";
        var parts = sb.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => p.StartsWith("DC=", StringComparison.OrdinalIgnoreCase))
            .Select(p => p[3..]);
        return string.Join(".", parts);
    }

    private List<string> ResolveDomainControllers()
    {
        // Öncelik: config'ten gelen liste (Ad:DomainControllers)
        var configured = _config.GetSection("Ad:DomainControllers").Get<string[]>();
        if (configured != null && configured.Length > 0)
            return configured.Where(d => !string.IsNullOrWhiteSpace(d)).ToList();

        // Fallback: LDAP server config (genelde DC FQDN'i)
        var ldap = _config["Ldap:Server"];
        if (!string.IsNullOrWhiteSpace(ldap))
            return new List<string> { ldap };

        return new List<string>();
    }

    private record LogonEvent(string TargetUser, string? WorkstationName, string? IpAddress, DateTime TimeCreated, long RecordId);

    private async Task<List<LogonEvent>> QueryDcAsync(string dc, HashSet<string> watchSet, CancellationToken ct)
    {
        long lastId = 0;
        bool isBootstrap = false;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();
            var cursor = await db.DcLogCursors.FirstOrDefaultAsync(c => c.DcName == dc, ct);
            if (cursor == null) { isBootstrap = true; }
            else lastId = cursor.LastRecordId;
        }

        var watchUsersScript = string.Join(",", watchSet.Select(u => $"'{u.Replace("'", "''")}'"));
        var dcArg = dc.Replace("'", "''");

        // Bootstrap: cursor yok — son 200 event'ten en yüksek RecordId'yi kaydet, hiçbir event işleme.
        // Sonraki tick'lerde incremental olarak yalnızca lastId'den büyükleri çekeriz.
        // XPath ile server-side RecordId filtre uygulanır — DC tarafında çok daha hızlı.
        string filterClause = isBootstrap
            ? "$xpath = \"*[System[(EventID=4768)]]\""
            : $"$xpath = \"*[System[(EventID=4768) and (EventRecordID > {lastId})]]\"";

        var script = $@"
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$dc = '{dcArg}'
$lastId = {lastId}
$isBootstrap = ${(isBootstrap ? "true" : "false")}
$watch = @({watchUsersScript})
$watchSet = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
foreach ($u in $watch) {{ [void]$watchSet.Add($u) }}

{filterClause}

try {{
    if ($isBootstrap) {{
        # Sadece son 50 event'in en yüksek RecordId'sini al, işleme — cursor'ı bugüne yetiştir
        $events = Get-WinEvent -ComputerName $dc -FilterXPath $xpath -MaxEvents 50 -LogName Security -ErrorAction Stop
    }} else {{
        $events = Get-WinEvent -ComputerName $dc -FilterXPath $xpath -MaxEvents 500 -LogName Security -ErrorAction Stop
    }}
}} catch {{
    Write-Output (ConvertTo-Json @{{ error = $_.Exception.Message; events = @(); maxRecordId = $lastId }})
    exit 0
}}

$results = @()
$maxId = $lastId
foreach ($e in $events) {{
    if ($e.RecordId -gt $maxId) {{ $maxId = $e.RecordId }}

    # Bootstrap modunda event işleme — sadece cursor'ı güncellemek için döngü
    if ($isBootstrap) {{ continue }}

    $xml = [xml]$e.ToXml()
    $data = @{{}}
    foreach ($d in $xml.Event.EventData.Data) {{
        $data[$d.Name] = $d.'#text'
    }}

    # Yalnızca başarılı TGT issue (status 0x0)
    if ($data['Status'] -and $data['Status'] -ne '0x0') {{ continue }}

    $user = $data['TargetUserName']
    if ([string]::IsNullOrWhiteSpace($user)) {{ continue }}
    # Computer account ve krbtgt'i atla
    if ($user.EndsWith('$')) {{ continue }}
    if ($user -eq 'krbtgt') {{ continue }}
    if (-not $watchSet.Contains($user)) {{ continue }}

    $ip = $data['IpAddress']
    # ::ffff:10.x.x.x -> 10.x.x.x
    if ($ip -and $ip.StartsWith('::ffff:')) {{ $ip = $ip.Substring(7) }}

    $results += [pscustomobject]@{{
        targetUser      = $user
        workstationName = $null   # 4768'de workstation hostname yok, sadece IP var
        ipAddress       = $ip
        timeCreated     = $e.TimeCreated.ToUniversalTime().ToString('o')
        recordId        = [int64]$e.RecordId
    }}
}}

ConvertTo-Json @{{ events = @($results); maxRecordId = $maxId }} -Depth 4 -Compress
";

        var tempPath = Path.Combine(Path.GetTempPath(), $"orch_userlogon_{Guid.NewGuid():N}.ps1");
        await File.WriteAllTextAsync(tempPath, script, ct);
        try
        {
            var (exitCode, output) = await RunPowerShellAsync(tempPath, TimeSpan.FromSeconds(120), ct);
            if (exitCode != 0)
            {
                _logger.LogWarning("[UserInstallWatcher] DC {Dc} query failed: exit={Code}, out={Out}", dc, exitCode, Truncate(output, 500));
                return new List<LogonEvent>();
            }

            if (string.IsNullOrWhiteSpace(output)) return new List<LogonEvent>();
            using var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var errEl) && errEl.ValueKind == JsonValueKind.String)
            {
                _logger.LogWarning("[UserInstallWatcher] DC {Dc} returned error: {Err}", dc, errEl.GetString());
                return new List<LogonEvent>();
            }

            var events = new List<LogonEvent>();
            if (root.TryGetProperty("events", out var evEl) && evEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in evEl.EnumerateArray())
                {
                    var user = e.GetProperty("targetUser").GetString() ?? "";
                    var ws = e.TryGetProperty("workstationName", out var w) ? w.GetString() : null;
                    var ip = e.TryGetProperty("ipAddress", out var i) ? i.GetString() : null;
                    if (ip == "-" || ip == "::1" || ip == "127.0.0.1") ip = null;
                    var ts = e.TryGetProperty("timeCreated", out var t) && t.ValueKind == JsonValueKind.String
                        ? DateTime.Parse(t.GetString()!).ToUniversalTime() : DateTime.UtcNow;
                    var rid = e.TryGetProperty("recordId", out var r) ? r.GetInt64() : 0;
                    events.Add(new LogonEvent(user, ws, ip, ts, rid));
                }
            }

            // Cursor güncelle
            var maxId = root.TryGetProperty("maxRecordId", out var mEl) ? mEl.GetInt64() : lastId;
            if (maxId > lastId)
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();
                var cursor = await db.DcLogCursors.FirstOrDefaultAsync(c => c.DcName == dc, ct);
                if (cursor == null)
                    db.DcLogCursors.Add(new DcLogCursor { DcName = dc, LastRecordId = maxId, UpdatedAt = DateTime.UtcNow });
                else
                {
                    cursor.LastRecordId = maxId;
                    cursor.UpdatedAt = DateTime.UtcNow;
                }
                await db.SaveChangesAsync(ct);
            }

            return events;
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
        }
    }

    private static string? TryReverseDns(string ip)
    {
        try
        {
            var entry = System.Net.Dns.GetHostEntry(ip);
            return entry.HostName;
        }
        catch { return null; }
    }

    private async Task<string?> TriggerInstallByIpAsync(string ip, CancellationToken ct)
    {
        var http = _httpClientFactory.CreateClient("internal");
        http.BaseAddress ??= new Uri(GetInternalBaseUrl());
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateSystemToken());

        var body = JsonSerializer.Serialize(new { ipAddress = ip });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        var resp = await http.PostAsync("/api/agent/remote-install", content, ct);
        var respText = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"install API {(int)resp.StatusCode}: {Truncate(respText, 300)}");

        try
        {
            using var doc = JsonDocument.Parse(respText);
            if (doc.RootElement.TryGetProperty("id", out var idEl)) return idEl.GetString();
            if (doc.RootElement.TryGetProperty("installId", out var idEl2)) return idEl2.GetString();
        }
        catch (JsonException) { }
        return null;
    }

    private async Task<string?> TriggerInstallAsync(string hostname, CancellationToken ct)
    {
        var http = _httpClientFactory.CreateClient("internal");
        http.BaseAddress ??= new Uri(GetInternalBaseUrl());
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateSystemToken());

        var body = JsonSerializer.Serialize(new { hostnames = new[] { hostname } });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        var resp = await http.PostAsync("/api/agent/remote-install/by-hostname", content, ct);
        var respText = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"install API {(int)resp.StatusCode}: {Truncate(respText, 300)}");

        try
        {
            using var doc = JsonDocument.Parse(respText);
            var first = doc.RootElement.GetProperty("results").EnumerateArray().FirstOrDefault();
            if (first.ValueKind == JsonValueKind.Object)
            {
                var ok = first.TryGetProperty("ok", out var okEl) && okEl.GetBoolean();
                if (!ok)
                {
                    var err = first.TryGetProperty("error", out var eEl) ? eEl.GetString() : "unknown";
                    throw new Exception($"install rejected: {err}");
                }
                return first.TryGetProperty("installId", out var idEl) ? idEl.GetString() : null;
            }
        }
        catch (JsonException) { }
        return null;
    }

    private string GetInternalBaseUrl()
    {
        return _config["Internal:BaseUrl"] ?? "http://localhost:5102";
    }

    private string GenerateSystemToken()
    {
        var key = Environment.GetEnvironmentVariable("JWT_SECRET_KEY")
            ?? _config["Jwt:Key"]
            ?? throw new InvalidOperationException("JWT_SECRET_KEY not set");
        if (key.StartsWith("${"))
            key = Environment.GetEnvironmentVariable("JWT_SECRET_KEY")
                ?? throw new InvalidOperationException("JWT_SECRET_KEY not set");

        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var creds = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, "system-user-install-watcher"),
            new Claim(ClaimTypes.Role, "Admin"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"] ?? "Orchestra",
            audience: _config["Jwt:Audience"] ?? "OrchestraUsers",
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static async Task<(int ExitCode, string Output)> RunPowerShellAsync(string scriptPath, TimeSpan timeout, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{scriptPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var proc = new Process { StartInfo = psi };
        proc.Start();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        try
        {
            await proc.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { if (!proc.HasExited) proc.Kill(true); } catch { }
            return (-1, "timeout");
        }

        var output = await stdoutTask;
        var stderr = await stderrTask;
        if (!string.IsNullOrWhiteSpace(stderr)) output += "\n[stderr]\n" + stderr;
        return (proc.ExitCode, output);
    }

    private static string Truncate(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= max ? s : s[..max] + "…";
    }
}
