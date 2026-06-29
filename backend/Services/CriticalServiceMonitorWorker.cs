using System.Collections.Concurrent;
using System.Management;
using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Data;
using Orchestra.Backend.Models;

namespace Orchestra.Backend.Services;

public sealed class CriticalServiceMonitorWorker : BackgroundService
{
    private const string WmiCheckServiceName = "__WMI_CIM__";
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(1);

    private static readonly MonitoredWindowsService[] DefaultServices =
    {
        new("MSSQL$SQLEXPRESS", "SQL Server (SQLEXPRESS)"),
        new("GeniusFileTransferServiceInbound", "GeniusFileTransferServiceInbound"),
        new("GeniusFileTransferServiceOutbound", "GeniusFileTransferServiceOutbound"),
        new("GeniusFullImporter", "GeniusFullImporter"),
        new("GeniusXMLExporter", "GeniusXMLExporter"),
        new("GeniusXMLImporter", "GeniusXMLImporter"),
        new("GeniusXMLReceiver", "GeniusXMLReceiver"),
        new("GeniusXMLSender", "GeniusXMLSender")
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CriticalServiceMonitorWorker> _log;
    private readonly ConcurrentDictionary<string, int> _failureStreaks = new(StringComparer.OrdinalIgnoreCase);

    // Dashboard'dan anlık tarama tetiklemek için
    private static volatile bool _triggerScan = false;
    public static void TriggerNow() => _triggerScan = true;

    // Son tarama zamanı (dashboard için)
    public static DateTime? LastScanAt { get; private set; }

    public CriticalServiceMonitorWorker(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<CriticalServiceMonitorWorker> log)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            _log.LogWarning("CriticalServiceMonitorWorker requires Windows WMI/CIM and will not run on this OS");
            return;
        }

        await Task.Delay(LoadOptions().StartupDelay, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            // Her döngüde ayarları yeniden oku (dashboard değişiklikleri anında devreye girer)
            var options = LoadOptions();

            if (!options.Enabled)
            {
                // Disable edilmişse kısa bekle ve tekrar dene
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                continue;
            }

            _triggerScan = false;
            var cycleStartedAt = DateTime.UtcNow;
            try
            {
                await RunCycleAsync(options, stoppingToken);
                LastScanAt = DateTime.UtcNow;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Critical service monitor cycle failed");
            }

            // İnterval dolana veya trigger gelene kadar bekle (5sn'de bir kontrol)
            var elapsed = DateTime.UtcNow - cycleStartedAt;
            var remaining = options.Interval - elapsed;
            while (remaining > TimeSpan.Zero && !_triggerScan && !stoppingToken.IsCancellationRequested)
            {
                var wait = remaining < TimeSpan.FromSeconds(5) ? remaining : TimeSpan.FromSeconds(5);
                await Task.Delay(wait, stoppingToken);
                elapsed = DateTime.UtcNow - cycleStartedAt;
                remaining = options.Interval - elapsed;
            }
        }

        _log.LogInformation("CriticalServiceMonitorWorker stopped");
    }

    [SupportedOSPlatform("windows")]
    private async Task RunCycleAsync(MonitorOptions options, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var serviceDefinitions = options.Services;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();

        // Defansif kapali-kontrol: Devices (agent registry) tablosunda da
        // IsTemporarilyClosed=true olan IP'ler taramadan dislanmali. UI iki
        // farkli endpoint kullaniyor, bu sayede hangisinden kapatilirsa kapatilsin
        // worker baglanti denemiyor.
        var closedIpsList = await db.Devices
            .AsNoTracking()
            .Where(d => d.IsTemporarilyClosed && d.IpAddress != "")
            .Select(d => d.IpAddress)
            .ToListAsync(ct);
        var closedIps = new HashSet<string>(closedIpsList, StringComparer.OrdinalIgnoreCase);

        var devices = await db.StoreDevices
            .AsNoTracking()
            .Where(d => !d.IsTemporarilyClosed && d.CalculatedIpAddress != "")
            .Select(d => new StoreDevice
            {
                DeviceId = d.DeviceId,
                StoreCode = d.StoreCode,
                StoreName = d.StoreName,
                DeviceType = d.DeviceType,
                DeviceName = d.DeviceName,
                CalculatedIpAddress = d.CalculatedIpAddress,
                IsTemporarilyClosed = d.IsTemporarilyClosed
            })
            .ToListAsync(ct);

        devices = devices
            .Where(d => options.DeviceTypes.Contains(d.DeviceType, StringComparer.OrdinalIgnoreCase))
            .Where(d => !closedIps.Contains(d.CalculatedIpAddress))
            .OrderBy(d => d.StoreCode)
            .ThenBy(d => d.DeviceName)
            .ToList();

        if (devices.Count == 0)
            return;

        var issues = new ConcurrentBag<ServiceIssue>();
        var healthyKeys = new ConcurrentBag<string>();
        var actionLogs = new ConcurrentBag<ActivityLog>();
        var checkedDeviceIds = devices.Select(d => d.DeviceId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        using var semaphore = new SemaphoreSlim(options.MaxConcurrency);
        var tasks = devices.Select(async device =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                var result = await Task.Run(
                    () => QueryRemoteServices(device, serviceDefinitions, options),
                    ct);

                if (!result.Success)
                {
                    issues.Add(ServiceIssue.ForWmiFailure(device, result.ErrorMessage ?? "WMI/CIM sorgusu basarisiz oldu."));
                    return;
                }

                healthyKeys.Add(BuildKey(device.DeviceId, WmiCheckServiceName));

                foreach (var definition in serviceDefinitions)
                {
                    if (result.Services.TryGetValue(definition.Name, out var service)
                        && service.State.Equals("Running", StringComparison.OrdinalIgnoreCase))
                    {
                        if (service.StartAttempted)
                            actionLogs.Add(CreateAutoStartActivityLog(device, definition, service));

                        healthyKeys.Add(BuildKey(device.DeviceId, definition.Name));
                        continue;
                    }

                    if (service != null && IsDisabled(service.StartMode))
                    {
                        healthyKeys.Add(BuildKey(device.DeviceId, definition.Name));
                        continue;
                    }

                    if (service == null)
                    {
                        issues.Add(ServiceIssue.ForMissingService(device, definition));
                    }
                    else
                    {
                        if (service.StartAttempted)
                            actionLogs.Add(CreateAutoStartActivityLog(device, definition, service));

                        issues.Add(ServiceIssue.ForStoppedService(device, definition, service));
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                issues.Add(ServiceIssue.ForWmiFailure(device, ex.Message));
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        var now = DateTime.UtcNow;
        var activeIncidents = await db.StoreServiceIncidents
            .Where(i => i.ResolvedAt == null)
            .ToListAsync(ct);

        var activeMap = activeIncidents
            .GroupBy(i => BuildKey(i.DeviceId, i.ServiceName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(i => i.LastDetectedAt).First(), StringComparer.OrdinalIgnoreCase);

        foreach (var healthyKey in healthyKeys.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            _failureStreaks.TryRemove(healthyKey, out _);

            if (activeMap.TryGetValue(healthyKey, out var incident))
            {
                incident.ResolvedAt = now;
                incident.LastDetectedAt = now;
                incident.Message = $"{incident.DisplayName} tekrar Running durumunda.";
            }
        }

        var openedOrUpdated = 0;
        foreach (var issue in issues.OrderBy(i => i.Device.StoreCode).ThenBy(i => i.Device.DeviceName).ThenBy(i => i.ServiceName))
        {
            var key = BuildKey(issue.Device.DeviceId, issue.ServiceName);
            var streak = _failureStreaks.AddOrUpdate(key, 1, (_, old) => old + 1);
            if (streak < options.ConfirmationThreshold)
                continue;

            if (activeMap.TryGetValue(key, out var incident))
            {
                UpdateIncident(incident, issue, now, streak);
            }
            else
            {
                incident = CreateIncident(issue, now, streak);
                db.StoreServiceIncidents.Add(incident);
                activeMap[key] = incident;
            }

            openedOrUpdated++;
        }

        foreach (var incident in activeIncidents.Where(i => !checkedDeviceIds.Contains(i.DeviceId)))
        {
            incident.ResolvedAt = now;
            incident.LastDetectedAt = now;
            incident.Message = "Cihaz artik servis izleme hedefleri arasinda degil.";
            _failureStreaks.TryRemove(BuildKey(incident.DeviceId, incident.ServiceName), out _);
        }

        if (!actionLogs.IsEmpty)
            db.ActivityLogs.AddRange(actionLogs);

        await db.SaveChangesAsync(ct);

        sw.Stop();
        _log.LogInformation(
            "Critical service scan completed: {DeviceCount} PC(s), {IssueCount} raw issue(s), {IncidentCount} active/updated incident(s), {ActionCount} auto-start action(s) in {Elapsed}ms",
            devices.Count,
            issues.Count,
            openedOrUpdated,
            actionLogs.Count,
            sw.ElapsedMilliseconds);
    }

    [SupportedOSPlatform("windows")]
    private static RemoteServiceQueryResult QueryRemoteServices(
        StoreDevice device,
        IReadOnlyList<MonitoredWindowsService> serviceDefinitions,
        MonitorOptions options)
    {
        try
        {
            var connectionOptions = BuildConnectionOptions(options);
            var scope = new ManagementScope($@"\\{device.CalculatedIpAddress}\root\cimv2", connectionOptions);
            scope.Connect();

            var where = string.Join(" OR ", serviceDefinitions.Select(s => $"Name='{EscapeWql(s.Name)}'"));
            var query = new ObjectQuery($"SELECT Name, DisplayName, State, StartMode FROM Win32_Service WHERE {where}");

            using var searcher = new ManagementObjectSearcher(scope, query)
            {
                Options = new System.Management.EnumerationOptions
                {
                    ReturnImmediately = false,
                    Timeout = options.WmiTimeout
                }
            };

            var services = new Dictionary<string, RemoteServiceSnapshot>(StringComparer.OrdinalIgnoreCase);
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                var name = Convert.ToString(item["Name"]) ?? "";
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                services[name] = ToSnapshot(item);
            }

            if (options.AutoStartStoppedServices)
            {
                foreach (var definition in serviceDefinitions)
                {
                    if (!services.TryGetValue(definition.Name, out var service))
                        continue;

                    if (service.State.Equals("Running", StringComparison.OrdinalIgnoreCase) || IsDisabled(service.StartMode))
                        continue;

                    services[definition.Name] = TryStartService(scope, definition.Name, options, service);
                }
            }

            return RemoteServiceQueryResult.Ok(services);
        }
        catch (Exception ex)
        {
            // WMI/DCOM erisilemiyor (cogunlukla dinamik RPC portu 49152-65535 firewall'da
            // kapali; port 135 acik olsa bile sorgu timeout olur). SMB/445 uzerinden sc.exe
            // ile fallback dene — bu yol saha PC'lerinde genelde acik kalir.
            try
            {
                return QueryViaSc(device, serviceDefinitions, options);
            }
            catch (Exception scEx)
            {
                return RemoteServiceQueryResult.Fail($"WMI: {ex.Message} | sc.exe: {scEx.Message}");
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static RemoteServiceSnapshot TryStartService(
        ManagementScope scope,
        string serviceName,
        MonitorOptions options,
        RemoteServiceSnapshot current)
    {
        try
        {
            using var serviceObject = GetServiceObject(scope, serviceName, options);
            if (serviceObject == null)
            {
            return current with
            {
                StartAttempted = true,
                PreviousState = current.State,
                StartMessage = "Start denenemedi: servis tekrar okunurken bulunamadi."
            };
            }

            using var result = serviceObject.InvokeMethod("StartService", null, null);
            var returnCode = Convert.ToUInt32(result?["ReturnValue"] ?? 999U);
            var message = DescribeStartReturnCode(returnCode);

            var deadline = DateTime.UtcNow.Add(options.ServiceStartTimeout);
            while (DateTime.UtcNow < deadline)
            {
                serviceObject.Get();
                var snapshot = ToSnapshot(serviceObject, true, returnCode, message, current.State);
                if (snapshot.State.Equals("Running", StringComparison.OrdinalIgnoreCase))
                    return snapshot;

                Thread.Sleep(options.ServiceStartPollInterval);
            }

            serviceObject.Get();
            return ToSnapshot(serviceObject, true, returnCode, message, current.State);
        }
        catch (Exception ex)
        {
            return current with
            {
                StartAttempted = true,
                PreviousState = current.State,
                StartMessage = $"StartService hatasi: {ex.Message}"
            };
        }
    }

    [SupportedOSPlatform("windows")]
    private static ManagementObject? GetServiceObject(ManagementScope scope, string serviceName, MonitorOptions options)
    {
        var query = new ObjectQuery($"SELECT Name, DisplayName, State, StartMode FROM Win32_Service WHERE Name='{EscapeWql(serviceName)}'");
        using var searcher = new ManagementObjectSearcher(scope, query)
        {
            Options = new System.Management.EnumerationOptions
            {
                ReturnImmediately = false,
                Timeout = options.WmiTimeout
            }
        };

        using var results = searcher.Get();
        foreach (ManagementObject item in results)
            return item;

        return null;
    }

    // ─── SMB / sc.exe fallback (WMI/DCOM erisilemediginde) ──────────────────────
    // Port 135 acik olsa bile dinamik RPC portu kapali sahalarda WMI timeout olur.
    // sc.exe \\ip ... SMB/445 + named-pipe uzerinden calistigindan bu yol genelde acik.
    // Backend servis hesabi (MUDODMN\mudoadmtd) kimligiyle kimlik dogrular.

    private static RemoteServiceQueryResult QueryViaSc(
        StoreDevice device,
        IReadOnlyList<MonitoredWindowsService> serviceDefinitions,
        MonitorOptions options)
    {
        var ip = device.CalculatedIpAddress;

        // Tek toplu cagri ile tum servis durumlarini al. Bazi sahalarda her sc/RPC cagrisi
        // sabit ~20sn surdugu icin (muhtemelen reverse-DNS/RPC binding gecikmesi) servisleri
        // tek tek sormak 8x ceza demek; tek "state= all" cagrisi cok daha ucuz. RunSc timeout'u
        // bu gecikmeyi karsilayacak kadar genis (asagi bkz).
        var (exit, output) = RunSc($@"\\{ip} query state= all", options.WmiTimeout);
        if (exit != 0)
            throw new InvalidOperationException($"sc.exe query basarisiz (exit {exit}): {output.Trim()}");

        var states = ParseScQueryAll(output);

        var services = new Dictionary<string, RemoteServiceSnapshot>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in serviceDefinitions)
        {
            if (!states.TryGetValue(definition.Name, out var state))
                continue;                           // kurulu degil -> caller "missing" isler
            services[definition.Name] = new RemoteServiceSnapshot(definition.Name, definition.DisplayName, state, "");
        }

        if (options.AutoStartStoppedServices)
        {
            foreach (var definition in serviceDefinitions)
            {
                if (!services.TryGetValue(definition.Name, out var service))
                    continue;
                if (service.State.Equals("Running", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Durmus servis: Disabled mi diye start tipini oku (yalnizca durmuslar icin).
                var (qcExit, qcOut) = RunSc($@"\\{ip} qc ""{definition.Name}""", options.WmiTimeout);
                var startMode = qcExit == 0
                    ? MapScStartType(MatchGroup(qcOut, @"START_TYPE\s*:\s*\d+\s+(\w+)"))
                    : "";
                service = service with { StartMode = startMode };

                if (IsDisabled(startMode))
                {
                    services[definition.Name] = service;   // Disabled -> caller saglikli sayar
                    continue;
                }

                services[definition.Name] = ScTryStartService(ip, definition.Name, options, service);
            }
        }

        return RemoteServiceQueryResult.Ok(services);
    }

    // "sc query state= all" ciktisini servis-adi -> normalize state sozlugune cevirir.
    private static Dictionary<string, string> ParseScQueryAll(string output)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var blocks = output.Split(new[] { "SERVICE_NAME:" }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var block in blocks)
        {
            var name = block.Split('\n', 2)[0].Trim();
            if (string.IsNullOrEmpty(name))
                continue;
            var state = NormalizeScState(MatchGroup(block, @"STATE\s*:\s*\d+\s+(\w+)"));
            if (!string.IsNullOrEmpty(state))
                dict[name] = state;
        }
        return dict;
    }

    private static RemoteServiceSnapshot ScTryStartService(
        string ip, string serviceName, MonitorOptions options, RemoteServiceSnapshot current)
    {
        var (exit, output) = RunSc($@"\\{ip} start ""{serviceName}""", options.WmiTimeout);
        var message = $"sc start exit {exit}. {output.Trim()}";

        var deadline = DateTime.UtcNow.Add(options.ServiceStartTimeout);
        while (DateTime.UtcNow < deadline)
        {
            var (qExit, qOut) = RunSc($@"\\{ip} query ""{serviceName}""", options.WmiTimeout);
            if (qExit == 0
                && NormalizeScState(MatchGroup(qOut, @"STATE\s*:\s*\d+\s+(\w+)"))
                    .Equals("Running", StringComparison.OrdinalIgnoreCase))
            {
                return current with
                {
                    State = "Running",
                    StartAttempted = true,
                    StartMessage = message,
                    PreviousState = current.State
                };
            }

            Thread.Sleep(options.ServiceStartPollInterval);
        }

        return current with
        {
            StartAttempted = true,
            StartMessage = message,
            PreviousState = current.State
        };
    }

    private static (int exitCode, string output) RunSc(string arguments, TimeSpan perCallTimeout)
    {
        try
        {
            using var proc = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            proc.Start();
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();

            // Bazi sahalarda her sc/RPC cagrisi sabit ~20sn suruyor; timeout'u bunu
            // karsilayacak kadar genis tut (WmiTimeout 8s -> 32s). Olu host'ta tek cagri
            // bu sureyle sinirli kalir.
            var waitMs = (int)Math.Clamp(perCallTimeout.TotalMilliseconds * 4, 15000, 40000);
            if (!proc.WaitForExit(waitMs))
            {
                try { proc.Kill(true); } catch { /* yok say */ }
                return (-1, "sc.exe zaman asimina ugradi.");
            }

            return (proc.ExitCode, stdoutTask.Result + stderrTask.Result);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }

    private static string MatchGroup(string input, string pattern)
    {
        var m = System.Text.RegularExpressions.Regex.Match(
            input, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : "";
    }

    private static string NormalizeScState(string raw) => raw.ToUpperInvariant() switch
    {
        "RUNNING" => "Running",
        "STOPPED" => "Stopped",
        "START_PENDING" => "StartPending",
        "STOP_PENDING" => "StopPending",
        "PAUSED" => "Paused",
        _ => raw
    };

    private static string MapScStartType(string raw) => raw.ToUpperInvariant() switch
    {
        "DISABLED" => "Disabled",
        "AUTO_START" => "Auto",
        "DELAYED" => "Auto",
        "DEMAND_START" => "Manual",
        "BOOT_START" => "Boot",
        "SYSTEM_START" => "System",
        _ => raw
    };

    [SupportedOSPlatform("windows")]
    private static RemoteServiceSnapshot ToSnapshot(
        ManagementBaseObject item,
        bool startAttempted = false,
        uint? startReturnCode = null,
        string? startMessage = null,
        string? previousState = null)
    {
        var name = Convert.ToString(item["Name"]) ?? "";
        return new RemoteServiceSnapshot(
            name,
            Convert.ToString(item["DisplayName"]) ?? name,
            Convert.ToString(item["State"]) ?? "Unknown",
            Convert.ToString(item["StartMode"]) ?? "",
            startAttempted,
            startReturnCode,
            startMessage,
            previousState);
    }

    [SupportedOSPlatform("windows")]
    private static ConnectionOptions BuildConnectionOptions(MonitorOptions options)
    {
        var connectionOptions = new ConnectionOptions
        {
            EnablePrivileges = true,
            Impersonation = ImpersonationLevel.Impersonate,
            Authentication = AuthenticationLevel.PacketPrivacy,
            Timeout = options.WmiTimeout
        };

        if (!string.IsNullOrWhiteSpace(options.Username) && !string.IsNullOrWhiteSpace(options.Password))
        {
            connectionOptions.Username = string.IsNullOrWhiteSpace(options.Domain)
                ? options.Username
                : $@"{options.Domain}\{options.Username}";
            connectionOptions.Password = options.Password;
        }

        return connectionOptions;
    }

    private MonitorOptions LoadOptions()
    {
        var section = _configuration.GetSection("CriticalServiceMonitor");
        var wmiSection = _configuration.GetSection("MudoSoft:Wmi");
        var passwordSecretKey = wmiSection["PasswordSecretKey"];

        var serviceDefinitions = section.GetSection("Services").Get<List<MonitoredWindowsService>>();
        if (serviceDefinitions == null || serviceDefinitions.Count == 0)
            serviceDefinitions = DefaultServices.ToList();

        var deviceTypes = section.GetSection("DeviceTypes").Get<string[]>();
        if (deviceTypes == null || deviceTypes.Length == 0)
            deviceTypes = new[] { "PC" };

        return new MonitorOptions
        {
            Enabled = section.GetValue("Enabled", true),
            Interval = TimeSpan.FromSeconds(Math.Max(15, section.GetValue("IntervalSeconds", (int)DefaultInterval.TotalSeconds))),
            StartupDelay = TimeSpan.FromSeconds(Math.Max(0, section.GetValue("StartupDelaySeconds", 15))),
            ConfirmationThreshold = Math.Max(1, section.GetValue("ConfirmationThreshold", 2)),
            MaxConcurrency = Math.Clamp(section.GetValue("MaxConcurrency", 8), 1, 32),
            WmiTimeout = TimeSpan.FromSeconds(Math.Clamp(section.GetValue("WmiTimeoutSeconds", 8), 2, 60)),
            AutoStartStoppedServices = section.GetValue("AutoStartStoppedServices", true),
            ServiceStartTimeout = TimeSpan.FromSeconds(Math.Clamp(section.GetValue("ServiceStartTimeoutSeconds", 20), 3, 120)),
            ServiceStartPollInterval = TimeSpan.FromMilliseconds(Math.Clamp(section.GetValue("ServiceStartPollIntervalMs", 1000), 250, 5000)),
            DeviceTypes = deviceTypes,
            Services = serviceDefinitions,
            Domain = wmiSection["Domain"] ?? "",
            Username = wmiSection["Username"] ?? "",
            Password = GetWmiPassword(passwordSecretKey, wmiSection)
        };
    }

    private static string GetWmiPassword(string? passwordSecretKey, IConfigurationSection wmiSection)
    {
        if (!string.IsNullOrWhiteSpace(passwordSecretKey))
        {
            var secretValue = Environment.GetEnvironmentVariable(passwordSecretKey);
            if (!string.IsNullOrWhiteSpace(secretValue))
                return secretValue;
        }

        return Environment.GetEnvironmentVariable("MUDOSOFT_WMI_PASSWORD")
            ?? Environment.GetEnvironmentVariable("WMI_PASSWORD")
            ?? wmiSection["Password"]
            ?? "";
    }

    private static string EscapeWql(string value) => value.Replace("'", "''");
    private static string BuildKey(string deviceId, string serviceName) => $"{deviceId}|{serviceName}";
    private static bool IsDisabled(string? startMode) =>
        string.Equals(startMode, "Disabled", StringComparison.OrdinalIgnoreCase);

    private static string DescribeStartReturnCode(uint code) => code switch
    {
        0 => "StartService basarili.",
        1 => "StartService desteklenmiyor.",
        2 => "Erisim reddedildi.",
        3 => "Bagimli servisler calismiyor.",
        4 => "Servis kontrol kodu gecersiz.",
        5 => "Servis istegi kabul edilemiyor.",
        6 => "Servis aktif degil.",
        7 => "Servis istegi zaman asimina ugradi.",
        8 => "Bilinmeyen hata.",
        9 => "Yol bulunamadi.",
        10 => "Servis zaten calisiyor.",
        11 => "Servis veritabani kilitli.",
        12 => "Servis bagimliligi silinmis.",
        13 => "Servis bagimliligi hatali.",
        14 => "Servis disabled.",
        15 => "Servis oturum acamadi.",
        16 => "Servis silinmek uzere.",
        17 => "Servis thread'i yok.",
        18 => "Dairesel servis bagimliligi.",
        19 => "Duplicate service name.",
        20 => "Gecersiz servis adi.",
        21 => "Gecersiz parametre.",
        22 => "Gecersiz servis hesabi.",
        23 => "Servis zaten mevcut.",
        24 => "Servis isaretlenmis durumda.",
        _ => $"StartService return code: {code}."
    };

    private static ActivityLog CreateAutoStartActivityLog(
        StoreDevice device,
        MonitoredWindowsService definition,
        RemoteServiceSnapshot service)
    {
        var success = service.State.Equals("Running", StringComparison.OrdinalIgnoreCase);
        var details = JsonSerializer.Serialize(new
        {
            device.DeviceId,
            device.StoreCode,
            device.StoreName,
            device.DeviceName,
            IpAddress = device.CalculatedIpAddress,
            ServiceName = definition.Name,
            DisplayName = string.IsNullOrWhiteSpace(service.DisplayName) ? definition.DisplayName : service.DisplayName,
            PreviousStatus = service.PreviousState ?? "Unknown",
            CurrentStatus = service.State,
            service.StartMode,
            service.StartReturnCode,
            service.StartMessage,
            Action = "AutoStartService"
        });

        return new ActivityLog
        {
            Username = "ServiceMonitor",
            Category = "ServiceMonitor",
            Action = "AutoStartService",
            Target = $"{device.DeviceId}/{definition.Name}",
            Details = details,
            Success = success,
            ErrorMessage = success ? null : service.StartMessage,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static StoreServiceIncident CreateIncident(ServiceIssue issue, DateTime now, int streak)
    {
        var incident = new StoreServiceIncident();
        UpdateIncident(incident, issue, now, streak);
        incident.FirstDetectedAt = now;
        return incident;
    }

    private static void UpdateIncident(StoreServiceIncident incident, ServiceIssue issue, DateTime now, int streak)
    {
        incident.DeviceId = issue.Device.DeviceId;
        incident.StoreCode = issue.Device.StoreCode;
        incident.StoreName = issue.Device.StoreName;
        incident.DeviceName = issue.Device.DeviceName;
        incident.IpAddress = issue.Device.CalculatedIpAddress;
        incident.ServiceName = issue.ServiceName;
        incident.DisplayName = issue.DisplayName;
        incident.Status = issue.Status;
        incident.Severity = issue.Severity;
        incident.Message = issue.Message;
        incident.LastStartMode = issue.StartMode;
        incident.LastError = issue.ErrorMessage;
        incident.ConsecutiveFailures = streak;
        incident.LastDetectedAt = now;
        incident.ResolvedAt = null;
    }

    private sealed class MonitorOptions
    {
        public bool Enabled { get; init; }
        public TimeSpan Interval { get; init; }
        public TimeSpan StartupDelay { get; init; }
        public int ConfirmationThreshold { get; init; }
        public int MaxConcurrency { get; init; }
        public TimeSpan WmiTimeout { get; init; }
        public bool AutoStartStoppedServices { get; init; }
        public TimeSpan ServiceStartTimeout { get; init; }
        public TimeSpan ServiceStartPollInterval { get; init; }
        public string[] DeviceTypes { get; init; } = Array.Empty<string>();
        public List<MonitoredWindowsService> Services { get; init; } = new();
        public string Domain { get; init; } = "";
        public string Username { get; init; } = "";
        public string Password { get; init; } = "";
    }

    private sealed class MonitoredWindowsService
    {
        public MonitoredWindowsService()
        {
        }

        public MonitoredWindowsService(string name, string displayName)
        {
            Name = name;
            DisplayName = displayName;
        }

        public string Name { get; set; } = "";
        public string DisplayName { get; set; } = "";
    }

    private sealed record RemoteServiceSnapshot(
        string Name,
        string DisplayName,
        string State,
        string StartMode,
        bool StartAttempted = false,
        uint? StartReturnCode = null,
        string? StartMessage = null,
        string? PreviousState = null);

    private sealed class RemoteServiceQueryResult
    {
        public bool Success { get; private init; }
        public string? ErrorMessage { get; private init; }
        public Dictionary<string, RemoteServiceSnapshot> Services { get; private init; } = new(StringComparer.OrdinalIgnoreCase);

        public static RemoteServiceQueryResult Ok(Dictionary<string, RemoteServiceSnapshot> services) =>
            new() { Success = true, Services = services };

        public static RemoteServiceQueryResult Fail(string errorMessage) =>
            new() { Success = false, ErrorMessage = errorMessage };
    }

    private sealed class ServiceIssue
    {
        public StoreDevice Device { get; init; } = new();
        public string ServiceName { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public string Status { get; init; } = "";
        public string Severity { get; init; } = "Critical";
        public string Message { get; init; } = "";
        public string? StartMode { get; init; }
        public string? ErrorMessage { get; init; }

        public static ServiceIssue ForStoppedService(
            StoreDevice device,
            MonitoredWindowsService definition,
            RemoteServiceSnapshot service)
        {
            return new ServiceIssue
            {
                Device = device,
                ServiceName = definition.Name,
                DisplayName = string.IsNullOrWhiteSpace(service.DisplayName) ? definition.DisplayName : service.DisplayName,
                Status = service.State,
                Severity = "Critical",
                Message = service.StartAttempted
                    ? $"{definition.DisplayName} servisi {service.State} durumunda. Otomatik start denendi ama Running dogrulanamadi."
                    : $"{definition.DisplayName} servisi {service.State} durumunda. Beklenen durum: Running.",
                StartMode = service.StartMode,
                ErrorMessage = service.StartMessage
            };
        }

        public static ServiceIssue ForMissingService(StoreDevice device, MonitoredWindowsService definition)
        {
            return new ServiceIssue
            {
                Device = device,
                ServiceName = definition.Name,
                DisplayName = definition.DisplayName,
                Status = "NotFound",
                Severity = "Critical",
                Message = $"{definition.DisplayName} servisi bulunamadi. Beklenen durum: Running."
            };
        }

        public static ServiceIssue ForWmiFailure(StoreDevice device, string error)
        {
            return new ServiceIssue
            {
                Device = device,
                ServiceName = WmiCheckServiceName,
                DisplayName = "WMI/CIM Erisimi",
                Status = "CheckFailed",
                Severity = "Warning",
                Message = "PC servisleri WMI/CIM ile kontrol edilemedi.",
                ErrorMessage = error
            };
        }
    }
}
