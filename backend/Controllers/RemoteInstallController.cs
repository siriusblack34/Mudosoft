using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Data;
using Orchestra.Backend.Models;
using System.Diagnostics;
using System.Text.Json;

namespace Orchestra.Backend.Controllers;

[ApiController]
[Authorize]
[Route("api/agent/remote-install")]
public class RemoteInstallController : ControllerBase
{
    private readonly ILogger<RemoteInstallController> _logger;
    private readonly IConfiguration _config;
    private readonly OrchestraDbContext _db;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CommandQueue _commandQueue;
    private readonly string _updatesPath;
    private readonly string _toolsPath;

    private static readonly Dictionary<string, InstallStatus> _installations = new();
    private static readonly object _lock = new();

    public RemoteInstallController(
        ILogger<RemoteInstallController> logger,
        IConfiguration config,
        OrchestraDbContext db,
        IServiceScopeFactory scopeFactory,
        CommandQueue commandQueue,
        IWebHostEnvironment env)
    {
        _logger = logger;
        _config = config;
        _db = db;
        _scopeFactory = scopeFactory;
        _commandQueue = commandQueue;
        _updatesPath = Path.Combine(env.ContentRootPath, "updates");
        _toolsPath = Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", "agent", "tools"));
    }

    /// <summary>
    /// DB'deki TÜM cihazları getirir — agent'lı ve agent'sız
    /// </summary>
    [HttpGet("devices")]
    public async Task<IActionResult> GetDevices()
    {
        var devices = await _db.Devices
            .AsNoTracking()
            .Select(d => new
            {
                d.Id,
                d.Hostname,
                d.IpAddress,
                d.StoreCode,
                d.StoreName,
                Type = d.Type.ToString(),
                d.Online,
                d.AgentVersion,
                HasAgent = d.AgentVersion != null && d.AgentVersion != ""
            })
            .OrderBy(d => d.StoreCode)
            .ThenBy(d => d.Hostname)
            .ToListAsync();

        return Ok(new
        {
            total = devices.Count,
            withAgent = devices.Count(d => d.HasAgent),
            withoutAgent = devices.Count(d => !d.HasAgent),
            devices
        });
    }

    /// <summary>
    /// Subnet taraması: Belirtilen subnet'te ping'e cevap veren makineleri bulur,
    /// DB'deki agent'lı cihazlarla karşılaştırır, agent'sız olanları işaretler.
    /// </summary>
    /// <summary>
    /// Ağ taraması: DeviceId verilirse agent üzerinden yerelde tarar (MAC+hostname alınır).
    /// DeviceId yoksa backend'den ping-only tarama yapar.
    /// </summary>
    [HttpPost("scan")]
    public async Task<IActionResult> ScanSubnet([FromBody] SubnetScanRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Subnet))
            return BadRequest(new { error = "Subnet gerekli (orn: 192.168.113)" });

        var subnet = request.Subnet.Trim().TrimEnd('.');
        var parts = subnet.Split('.');
        if (parts.Length < 2 || parts.Length > 3)
            return BadRequest(new { error = "Subnet formatı: 192.168.113 veya 10.0.102" });

        // EnrichOnly: Frontend'den gelen raw cihazlara vendor + agent bilgisi ekle
        if (request.EnrichOnly && request.RawDevices != null)
        {
            var enrichDevices = await _db.Devices.AsNoTracking()
                .Where(d => d.IpAddress.StartsWith(subnet + "."))
                .Select(d => new { d.IpAddress, d.Hostname, d.AgentVersion, d.Online, d.StoreCode, Type = d.Type.ToString() })
                .ToListAsync();
            var enrichMap = enrichDevices.ToDictionary(d => d.IpAddress);

            var enriched = request.RawDevices.Select(raw =>
            {
                var hasAgent = enrichMap.TryGetValue(raw.IpAddress, out var dev);
                return new SubnetScanResult
                {
                    IpAddress = raw.IpAddress,
                    Reachable = true,
                    Hostname = hasAgent ? dev!.Hostname : raw.Hostname,
                    MacAddress = raw.MacAddress,
                    Vendor = raw.MacAddress != null ? LookupMacVendor(raw.MacAddress) : null,
                    HasAgent = hasAgent,
                    AgentVersion = hasAgent ? dev!.AgentVersion : null,
                    Online = hasAgent && dev!.Online,
                    StoreCode = hasAgent ? dev!.StoreCode : 0,
                    DeviceType = hasAgent ? dev!.Type : null,
                    PingMs = raw.PingMs
                };
            })
            .OrderBy(r => System.Net.IPAddress.Parse(r.IpAddress).GetAddressBytes()[3])
            .ToList();

            return Ok(new
            {
                subnet,
                scannedRange = $"{subnet}.1 - {subnet}.254",
                total = enriched.Count,
                withAgent = enriched.Count(r => r.HasAgent),
                withoutAgent = enriched.Count(r => !r.HasAgent),
                devices = enriched
            });
        }

        var startIp = request.StartIp ?? 1;
        var endIp = request.EndIp ?? 254;

        // DB'den bu subnet'teki mevcut agent'lı cihazları al
        var existingDevices = await _db.Devices.AsNoTracking()
            .Where(d => d.IpAddress.StartsWith(subnet + "."))
            .Select(d => new { d.IpAddress, d.Hostname, d.AgentVersion, d.Online, d.StoreCode, Type = d.Type.ToString() })
            .ToListAsync();
        var agentMap = existingDevices.ToDictionary(d => d.IpAddress);

        // Agent-based tarama: Yerelde çalışan script gönder
        if (!string.IsNullOrWhiteSpace(request.DeviceId))
        {
            var device = await _db.Devices.FindAsync(request.DeviceId);
            if (device == null || !device.Online)
                return BadRequest(new { error = "Cihaz bulunamadı veya çevrimdışı" });

            // Script'i run-script mekanizmasıyla gönder (kanıtlanmış çalışan yol)
            var script = BuildScanScript(subnet, startIp, endIp);
            var commandId = Guid.NewGuid();
            _commandQueue.Enqueue(new Orchestra.Shared.Dtos.CommandDto
            {
                Id = commandId,
                DeviceId = request.DeviceId,
                Type = Orchestra.Shared.Enums.CommandType.ExecuteScript,
                Payload = script,
                CreatedAtUtc = DateTime.UtcNow
            });

            _logger.LogInformation("IP Scan script gönderildi: Device={DeviceId} CommandId={CommandId}", request.DeviceId, commandId);

            // Sonucu bekle (max 240sn — bazı mağaza ağlarında tüm /24 tarama belirgin daha uzun sürebiliyor)
            string? output = null;
            for (int i = 0; i < 300; i++)
            {
                await Task.Delay(1000);
                using var scope = _scopeFactory.CreateScope();
                var scopedDb = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();
                var result = await scopedDb.CommandResultRecords
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r => r.CommandId == commandId);
                if (result != null)
                {
                    output = result.Output;
                    _logger.LogInformation("IP Scan sonucu geldi: {Len} bytes", output?.Length ?? 0);
                    break;
                }
            }

            if (output == null)
                return Ok(new { subnet, scannedRange = $"{subnet}.{startIp} - {subnet}.{endIp}", total = 0, devices = Array.Empty<object>(), error = "Zaman aşımı — agent yanıt vermedi" });

            if (string.IsNullOrWhiteSpace(output))
                return Ok(new
                {
                    subnet,
                    scannedRange = $"{subnet}.{startIp} - {subnet}.{endIp}",
                    total = 0,
                    devices = Array.Empty<object>(),
                    error = "Tarama çıktısı boş döndü. Bu cihazda eski tarama yöntemi uyumsuz olabilir."
                });

            // Parse pipe-delimited text: IP|MAC|HOSTNAME|PING_MS
            var scanResults = new List<SubnetScanResult>();
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var cols = line.Trim().Split('|');
                if (cols.Length < 4 || !cols[0].Contains('.')) continue;

                var ip = cols[0].Trim();
                var mac = string.IsNullOrWhiteSpace(cols[1]) ? null : cols[1].Trim();
                var hostname = string.IsNullOrWhiteSpace(cols[2]) ? null : cols[2].Trim();
                int.TryParse(cols[3].Trim(), out var pingMs);

                var hasAgent = agentMap.TryGetValue(ip, out var dev);
                scanResults.Add(new SubnetScanResult
                {
                    IpAddress = ip,
                    Reachable = true,
                    Hostname = hasAgent ? dev!.Hostname : hostname,
                    MacAddress = mac,
                    Vendor = mac != null ? LookupMacVendor(mac) : null,
                    HasAgent = hasAgent,
                    AgentVersion = hasAgent ? dev!.AgentVersion : null,
                    Online = hasAgent && dev!.Online,
                    StoreCode = hasAgent ? dev!.StoreCode : 0,
                    DeviceType = hasAgent ? dev!.Type : null,
                    PingMs = pingMs
                });
            }

            if (scanResults.Count == 0 && output.Contains("Hata:", StringComparison.OrdinalIgnoreCase))
                return Ok(new
                {
                    subnet,
                    scannedRange = $"{subnet}.{startIp} - {subnet}.{endIp}",
                    total = 0,
                    devices = Array.Empty<object>(),
                    error = output.Trim()
                });

            var agentSorted = scanResults.OrderBy(r =>
                System.Net.IPAddress.Parse(r.IpAddress).GetAddressBytes()[3]
            ).ToList();

            return Ok(new
            {
                subnet,
                scannedRange = $"{subnet}.{startIp} - {subnet}.{endIp}",
                total = agentSorted.Count,
                withAgent = agentSorted.Count(r => r.HasAgent),
                withoutAgent = agentSorted.Count(r => !r.HasAgent),
                devices = agentSorted
            });
        }

        // Fallback: Backend'den ping-only tarama (agent yoksa)
        var pingResults = new System.Collections.Concurrent.ConcurrentBag<SubnetScanResult>();
        var pingTasks = new List<Task>();

        for (int i = startIp; i <= endIp; i++)
        {
            var ip = $"{subnet}.{i}";
            pingTasks.Add(Task.Run(async () =>
            {
                try
                {
                    using var ping = new System.Net.NetworkInformation.Ping();
                    var reply = await ping.SendPingAsync(ip, 1500);
                    if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
                    {
                        var hasAgent = agentMap.TryGetValue(ip, out var dev);
                        pingResults.Add(new SubnetScanResult
                        {
                            IpAddress = ip,
                            Reachable = true,
                            Hostname = hasAgent ? dev!.Hostname : await TryResolveHostname(ip),
                            HasAgent = hasAgent,
                            Online = hasAgent && dev!.Online,
                            StoreCode = hasAgent ? dev!.StoreCode : 0,
                            PingMs = (int)reply.RoundtripTime
                        });
                    }
                }
                catch { }
            }));
        }
        await Task.WhenAll(pingTasks);

        var sorted = pingResults.OrderBy(r => System.Net.IPAddress.Parse(r.IpAddress).GetAddressBytes()[3]).ToList();
        return Ok(new
        {
            subnet,
            scannedRange = $"{subnet}.{startIp} - {subnet}.{endIp}",
            total = sorted.Count,
            withAgent = sorted.Count(r => r.HasAgent),
            withoutAgent = sorted.Count(r => !r.HasAgent),
            devices = sorted
        });
    }

    private static string BuildScanScript(string subnet, int startIp, int endIp)
    {
        // Hibrit tarama:
        // 1) Yeni makinelerde hızlı async SendPingAsync
        // 2) Sonuç çıkmazsa Win7/uyumluluk için ping.exe fallback
        return "$ErrorActionPreference='SilentlyContinue'\r\n" +
               "$s='" + subnet + "'\r\n" +
               "$alive=@()\r\n" +
               "$fastTasks=@()\r\n" +
               "try {\r\n" +
               "    foreach($i in " + startIp + ".." + endIp + "){\r\n" +
               "        $ip=\"$s.$i\"\r\n" +
               "        $p=New-Object Net.NetworkInformation.Ping\r\n" +
               "        $fastTasks += [PSCustomObject]@{IP=$ip; T=$p.SendPingAsync($ip,800)}\r\n" +
               "    }\r\n" +
               "    $taskList = @($fastTasks | ForEach-Object { $_.T })\r\n" +
               "    if($taskList.Count -gt 0){ try { [Threading.Tasks.Task]::WaitAll($taskList, 6000) | Out-Null } catch {} }\r\n" +
               "    foreach($t in $fastTasks){\r\n" +
               "        if($t.T -and $t.T.IsCompleted -and -not $t.T.IsFaulted -and $t.T.Result -and $t.T.Result.Status -eq 'Success'){\r\n" +
               "            $alive += [PSCustomObject]@{IP=$t.IP; Ms=[int]$t.T.Result.RoundtripTime}\r\n" +
               "        }\r\n" +
               "    }\r\n" +
               "} catch {}\r\n" +
               "if($alive.Count -eq 0){\r\n" +
               "    foreach($i in " + startIp + ".." + endIp + "){\r\n" +
               "        $ip=\"$s.$i\"\r\n" +
               "        $sw=[Diagnostics.Stopwatch]::StartNew()\r\n" +
               "        & ping.exe -n 1 -w 180 $ip | Out-Null\r\n" +
               "        $sw.Stop()\r\n" +
               "        if($LASTEXITCODE -eq 0){\r\n" +
               "            $alive += [PSCustomObject]@{IP=$ip; Ms=[Math]::Max([int]$sw.ElapsedMilliseconds,0)}\r\n" +
               "        }\r\n" +
               "    }\r\n" +
               "}\r\n" +
               "foreach($a in @($alive)){\r\n" +
               "    try { & ping.exe -n 1 -w 200 $a.IP | Out-Null } catch {}\r\n" +
               "}\r\n" +
               "$mac=@{}\r\n" +
               "arp -a | ForEach-Object {\r\n" +
               "    if($_ -match '(\\d+\\.\\d+\\.\\d+\\.\\d+)\\s+([\\da-fA-F]{2}-[\\da-fA-F]{2}-[\\da-fA-F]{2}-[\\da-fA-F]{2}-[\\da-fA-F]{2}-[\\da-fA-F]{2})') {\r\n" +
               "        $mac[$Matches[1]]=$Matches[2].Replace('-',':').ToUpper()\r\n" +
               "    }\r\n" +
               "}\r\n" +
               "function Resolve-HostFast([string]$ip){\r\n" +
               "    try {\r\n" +
               "        $first = (ping.exe -a -n 1 -w 250 $ip | Select-Object -First 1)\r\n" +
               "        if($first -and $first -match '\\[(\\d+\\.\\d+\\.\\d+\\.\\d+)\\]'){\r\n" +
               "            $left = $first.Substring(0, $first.IndexOf('[')).Trim()\r\n" +
               "            $parts = $left -split '\\s+'\r\n" +
               "            if($parts.Length -gt 0){\r\n" +
               "                $candidate = $parts[$parts.Length - 1].Trim()\r\n" +
               "                if($candidate -and $candidate -ne $ip){ return $candidate }\r\n" +
               "            }\r\n" +
               "        }\r\n" +
               "    } catch {}\r\n" +
               "    try {\r\n" +
               "        $nbtLine = (nbtstat -A $ip | Select-String '<00>\\s+UNIQUE' | Select-Object -First 1)\r\n" +
               "        if($nbtLine){\r\n" +
               "            $candidate = (($nbtLine.ToString().Trim() -split '\\s+')[0]).Trim()\r\n" +
               "            if($candidate -and $candidate -notmatch '^[-<]'){ return $candidate }\r\n" +
               "        }\r\n" +
               "    } catch {}\r\n" +
               "    return ''\r\n" +
               "}\r\n" +
               "try {\r\n" +
               "Add-Type -TypeDefinition @\"\r\n" +
               "using System;\r\n" +
               "using System.Net;\r\n" +
               "using System.Runtime.InteropServices;\r\n" +
               "public static class MudArp {\r\n" +
               "    [DllImport(\"iphlpapi.dll\", ExactSpelling=true)]\r\n" +
               "    public static extern int SendARP(int destIp, int srcIp, byte[] macAddr, ref int phyAddrLen);\r\n" +
               "    public static string GetMac(string ipString) {\r\n" +
               "        var ip = IPAddress.Parse(ipString).GetAddressBytes();\r\n" +
               "        var mac = new byte[6];\r\n" +
               "        int len = mac.Length;\r\n" +
               "        int result = SendARP(BitConverter.ToInt32(ip, 0), 0, mac, ref len);\r\n" +
               "        if (result != 0 || len <= 0) return string.Empty;\r\n" +
               "        string[] parts = new string[len];\r\n" +
               "        for (int i = 0; i < len; i++) parts[i] = mac[i].ToString(\"X2\");\r\n" +
               "        return string.Join(\":\", parts);\r\n" +
               "    }\r\n" +
               "}\r\n" +
               "\"@ | Out-Null\r\n" +
               "} catch {}\r\n" +
               "function Get-MacFast([string]$ip){\r\n" +
               "    try { return [MudArp]::GetMac($ip) } catch { return '' }\r\n" +
               "}\r\n" +
               "$resolveNames=$alive.Count -le 12\r\n" +
               "$lines=@()\r\n" +
               "foreach($a in $alive){\r\n" +
               "    $ip=$a.IP\r\n" +
               "    $m=$null\r\n" +
               "    try { $m=$a.Mac } catch {}\r\n" +
               "    if(-not $m){ $m=Get-MacFast $ip }\r\n" +
               "    if(-not $m){ $m=$mac[$ip] }\r\n" +
               "    if(-not $m){\r\n" +
               "        try { & ping.exe -n 1 -w 200 $ip | Out-Null } catch {}\r\n" +
               "        try {\r\n" +
               "            foreach($arpRow in @(arp -a $ip)){\r\n" +
               "                if($arpRow -match '([\\da-fA-F]{2}-[\\da-fA-F]{2}-[\\da-fA-F]{2}-[\\da-fA-F]{2}-[\\da-fA-F]{2}-[\\da-fA-F]{2})'){\r\n" +
               "                    $m = $Matches[1].Replace('-',':').ToUpper()\r\n" +
               "                    break\r\n" +
               "                }\r\n" +
               "            }\r\n" +
               "        } catch {}\r\n" +
               "    }\r\n" +
               "    $h=''\r\n" +
               "    $shouldResolve=$resolveNames\r\n" +
               "    if(-not $shouldResolve -and $m){\r\n" +
               "        $prefix=$m.Substring(0,[Math]::Min(8,$m.Length)).ToUpper()\r\n" +
               "        if(@('24:2F:FA','54:7F:54','B4:00:16','00:07:81','00:1D:25','00:07:4D','00:A0:F8','00:17:FC') -contains $prefix){ $shouldResolve=$true }\r\n" +
               "    }\r\n" +
               "    if($shouldResolve){ $h=Resolve-HostFast $ip }\r\n" +
               "    $lines += \"$ip|$m|$h|$($a.Ms)\"\r\n" +
               "}\r\n" +
               "$lines -join \"`n\"\r\n";
    }

    private static string? LookupMacVendor(string mac)
    {
        if (string.IsNullOrWhiteSpace(mac) || mac.Length < 8) return null;
        var prefix = mac.Substring(0, 8).ToUpperInvariant(); // "AA:BB:CC"
        return MacVendors.TryGetValue(prefix, out var vendor) ? vendor : null;
    }

    // Yaygın MAC OUI prefix'leri — ağ cihazları, POS, PC, yazıcı vb.
    private static readonly Dictionary<string, string> MacVendors = new(StringComparer.OrdinalIgnoreCase)
    {
        // Ağ cihazları
        ["00:1A:2B"] = "Ayga (Türk)", ["00:23:89"] = "Zyxel", ["00:1D:AA"] = "Zyxel",
        ["00:A0:C5"] = "Zyxel", ["B0:B2:DC"] = "Zyxel", ["40:4A:03"] = "Zyxel",
        ["D4:F5:27"] = "Zyxel", ["E4:18:6B"] = "Zyxel",
        ["00:17:C5"] = "SonicWall", ["00:06:B1"] = "SonicWall",
        ["00:1E:58"] = "D-Link", ["1C:7E:E5"] = "D-Link", ["28:10:7B"] = "D-Link",
        ["F0:B4:D2"] = "D-Link", ["00:50:7F"] = "DrayTek",
        ["00:1F:33"] = "Netgear", ["08:BD:43"] = "Netgear", ["20:E5:2A"] = "Netgear",
        ["44:94:FC"] = "Netgear", ["A0:21:B7"] = "Netgear",
        ["00:18:0A"] = "Cisco", ["00:1A:A1"] = "Cisco", ["24:81:3B"] = "Cisco",
        ["00:1F:CA"] = "Cisco",
        ["00:50:56"] = "VMware",
        ["00:0C:29"] = "VMware", ["00:15:5D"] = "Hyper-V",
        ["00:1C:B3"] = "Apple", ["3C:22:FB"] = "Apple", ["A8:5C:2C"] = "Apple",
        ["F0:18:98"] = "Apple", ["AC:DE:48"] = "Apple",
        // TP-Link
        ["50:C7:BF"] = "TP-Link", ["54:C8:0F"] = "TP-Link", ["C0:25:E9"] = "TP-Link",
        ["EC:08:6B"] = "TP-Link", ["14:EB:B6"] = "TP-Link", ["30:DE:4B"] = "TP-Link",
        ["60:32:B1"] = "TP-Link", ["68:FF:7B"] = "TP-Link", ["B0:4E:26"] = "TP-Link",
        ["98:DA:C4"] = "TP-Link", ["AC:84:C6"] = "TP-Link", ["18:D6:C7"] = "TP-Link",
        ["5C:E9:31"] = "TP-Link", ["C0:4A:00"] = "TP-Link", ["30:B5:C2"] = "TP-Link",
        ["8C:80:63"] = "TP-Link",
        // MikroTik
        ["00:0C:42"] = "MikroTik", ["2C:C8:1B"] = "MikroTik", ["48:A9:8A"] = "MikroTik",
        ["4C:5E:0C"] = "MikroTik", ["6C:3B:6B"] = "MikroTik", ["74:4D:28"] = "MikroTik",
        ["CC:2D:E0"] = "MikroTik", ["D4:01:C3"] = "MikroTik", ["E4:8D:8C"] = "MikroTik",
        // Ubiquiti
        ["04:18:D6"] = "Ubiquiti", ["24:5A:4C"] = "Ubiquiti", ["68:72:51"] = "Ubiquiti",
        ["78:8A:20"] = "Ubiquiti", ["80:2A:A8"] = "Ubiquiti", ["F0:9F:C2"] = "Ubiquiti",
        // HP / HPE
        ["00:1A:4B"] = "HP", ["00:21:5A"] = "HP", ["08:00:09"] = "HP",
        ["10:1F:74"] = "HP", ["14:58:D0"] = "HP", ["2C:41:38"] = "HP",
        ["3C:D9:2B"] = "HP", ["80:C1:6E"] = "HP", ["94:57:A5"] = "HP",
        ["B4:B5:2F"] = "HP", ["D4:C9:EF"] = "HP", ["88:51:FB"] = "HP",
        ["50:81:40"] = "HP",
        // Dell
        ["00:14:22"] = "Dell", ["00:1A:A0"] = "Dell", ["14:FE:B5"] = "Dell",
        ["18:A9:9B"] = "Dell", ["24:6E:96"] = "Dell", ["34:17:EB"] = "Dell",
        ["44:A8:42"] = "Dell", ["B0:83:FE"] = "Dell", ["F4:8E:38"] = "Dell",
        ["F8:BC:12"] = "Dell", ["E4:43:4B"] = "Dell",
        // Lenovo
        ["00:06:1B"] = "Lenovo", ["50:7B:9D"] = "Lenovo", ["54:E1:AD"] = "Lenovo",
        ["70:5A:0F"] = "Lenovo", ["8C:16:45"] = "Lenovo", ["98:FA:9B"] = "Lenovo",
        // Intel (NIC)
        ["00:1B:21"] = "Intel", ["3C:97:0E"] = "Intel", ["68:05:CA"] = "Intel",
        ["A4:BB:6D"] = "Intel", ["E8:6A:64"] = "Intel", ["8C:EC:4B"] = "Intel",
        // Realtek
        ["00:E0:4C"] = "Realtek", ["52:54:00"] = "Realtek/QEMU", ["00:20:18"] = "Realtek",
        // Yazıcılar
        ["00:00:48"] = "Epson", ["00:26:AB"] = "Epson", ["64:EB:8C"] = "Epson",
        ["AC:18:26"] = "Epson", ["C4:36:55"] = "Epson",
        ["00:15:99"] = "Samsung", ["00:16:32"] = "Samsung", ["00:1A:8A"] = "Samsung",
        ["00:1E:E1"] = "Samsung", ["34:C3:AC"] = "Samsung",
        ["00:1B:A9"] = "Brother", ["00:80:77"] = "Brother", ["30:05:5C"] = "Brother",
        ["00:00:74"] = "Ricoh", ["00:26:73"] = "Ricoh",
        // Zebra (POS yazıcı)
        ["00:07:4D"] = "Zebra", ["00:A0:F8"] = "Zebra", ["24:2F:FA"] = "Toshiba",
        // Ingenico / Verifone (POS terminali)
        ["00:07:81"] = "Ingenico", ["00:1D:25"] = "Ingenico", ["B4:00:16"] = "Ingenico",
        ["54:7F:54"] = "Ingenico",
        ["00:17:7B"] = "Verifone",
        ["00:17:FC"] = "Suprema",
        ["00:40:8C"] = "Axis",
        // Hikvision / Dahua (kamera)
        ["44:19:B6"] = "Hikvision", ["54:C4:15"] = "Hikvision", ["C0:56:E3"] = "Hikvision",
        ["BC:AD:28"] = "Hikvision", ["28:57:BE"] = "Hikvision",
        ["3C:EF:8C"] = "Dahua", ["4C:11:BF"] = "Dahua", ["A0:BD:1D"] = "Dahua",
        // Huawei
        ["00:E0:FC"] = "Huawei", ["48:46:FB"] = "Huawei", ["70:7B:E8"] = "Huawei",
        ["88:3F:D3"] = "Huawei", ["CC:A2:23"] = "Huawei",
        // Aruba
        ["00:0B:86"] = "Aruba", ["00:1A:1E"] = "Aruba", ["24:DE:C6"] = "Aruba",
        ["40:E3:D6"] = "Aruba", ["94:B4:0F"] = "Aruba",
        ["FC:F5:28"] = "Zyxel",
    };

    private static async Task<string?> TryNetBiosLookup(string ip)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("nbtstat", $"-A {ip}")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null) return null;

            var cts = new CancellationTokenSource(3000);
            var output = await proc.StandardOutput.ReadToEndAsync(cts.Token);
            try { proc.Kill(); } catch { }

            // nbtstat çıktısında ilk satır genelde: HOSTNAME       <00>  UNIQUE
            foreach (var line in output.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Contains("<00>") && trimmed.Contains("UNIQUE"))
                {
                    var parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 0 && !parts[0].StartsWith(".."))
                        return parts[0].Trim();
                }
            }
        }
        catch { }
        return null;
    }

    private static async Task<Dictionary<string, string>> GetArpTableAsync()
    {
        var map = new Dictionary<string, string>();
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("arp", "-a")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null) return map;
            var output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();

            foreach (var line in output.Split('\n'))
            {
                var parts = line.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3 && parts[1].Contains('-'))
                {
                    var ip = parts[0].Trim();
                    var mac = parts[1].Trim().Replace('-', ':').ToUpperInvariant();
                    if (!mac.Equals("FF:FF:FF:FF:FF:FF", StringComparison.OrdinalIgnoreCase))
                        map[ip] = mac;
                }
            }
        }
        catch { /* ARP okunamazsa devam et */ }
        return map;
    }

    private async Task<string?> TryResolveHostname(string ip)
    {
        try
        {
            var entry = await System.Net.Dns.GetHostEntryAsync(ip);
            return entry.HostName;
        }
        catch { return null; }
    }

    [HttpPost]
    public IActionResult StartInstall([FromBody] RemoteInstallRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.IpAddress))
            return BadRequest(new { error = "IP adresi gerekli" });

        if (!System.Net.IPAddress.TryParse(request.IpAddress.Trim(), out _))
            return BadRequest(new { error = "Geçersiz IP adresi" });

        var ip = request.IpAddress.Trim();

        var storeCode = request.StoreCode?.Trim();
        if (string.IsNullOrEmpty(storeCode))
        {
            var parts = ip.Split('.');
            if (parts.Length == 4 && int.TryParse(parts[2], out var octet) && octet > 1)
                storeCode = octet.ToString();
            else
                return BadRequest(new { error = "Mağaza kodu tespit edilemedi, manuel girin" });
        }

        lock (_lock)
        {
            if (_installations.TryGetValue(ip, out var existing) && existing.Phase == "running")
                return Conflict(new { error = "Bu IP için zaten bir kurulum devam ediyor" });
        }

        var latestFile = Path.Combine(_updatesPath, "latest.json");
        if (!System.IO.File.Exists(latestFile))
            return BadRequest(new { error = "Agent paketi bulunamadı. Önce bir build yapın." });

        var latestJson = System.IO.File.ReadAllText(latestFile);
        var latest = JsonSerializer.Deserialize<JsonElement>(latestJson);
        var fileName = latest.GetProperty("fileName").GetString();
        var zipPath = Path.Combine(_updatesPath, fileName!);
        if (!System.IO.File.Exists(zipPath))
            return BadRequest(new { error = $"Agent ZIP bulunamadı: {fileName}" });

        // Backend URL — agent bu URL'e bağlanacak
        var backendUrl = _config["Agent:BackendUrl"] ?? "http://10.0.210.99:5102";

        var installId = Guid.NewGuid().ToString("N")[..8];
        var status = new InstallStatus
        {
            Id = installId,
            IpAddress = ip,
            StoreCode = storeCode,
            Phase = "running",
            Steps = new List<InstallStep>(),
            StartedAt = DateTime.UtcNow
        };

        lock (_lock) { _installations[ip] = status; }

        _ = Task.Run(() => RunInstallAsync(status, ip, storeCode, backendUrl, zipPath));

        return Ok(new { installId, ip, storeCode, message = "Kurulum başlatıldı" });
    }

    [HttpGet("status")]
    public IActionResult GetStatus([FromQuery] string ip)
    {
        lock (_lock)
        {
            if (_installations.TryGetValue(ip, out var status))
                return Ok(status);
        }
        return NotFound(new { error = "Kurulum kaydı bulunamadı" });
    }

    [HttpGet("history")]
    public IActionResult GetHistory()
    {
        lock (_lock)
        {
            return Ok(_installations.Values
                .OrderByDescending(s => s.StartedAt)
                .Take(50)
                .ToList());
        }
    }

    private async Task RunInstallAsync(
        InstallStatus status, string ip, string storeCode, string backendUrl, string zipPath)
    {
        void AddStep(string name, string state, string? detail = null)
        {
            var step = status.Steps.FirstOrDefault(s => s.Name == name);
            if (step != null) { step.State = state; step.Detail = detail ?? step.Detail; }
            else status.Steps.Add(new InstallStep { Name = name, State = state, Detail = detail ?? "" });
        }

        try
        {
            // Step 1: Ping
            AddStep("Bağlantı testi", "running");
            var pingOk = await TestPing(ip);
            if (!pingOk)
            {
                AddStep("Bağlantı testi", "error", "Makineye erişilemiyor");
                status.Phase = "error"; status.Error = "Ping başarısız";
                return;
            }
            AddStep("Bağlantı testi", "done", "Ping başarılı");

            // Step 2: SMB admin share test
            AddStep("SMB erişim testi", "running");
            var (smbOk, smbDetail) = await TestSmb(ip);
            if (!smbOk)
            {
                AddStep("SMB erişim testi", "error", smbDetail);
                status.Phase = "error"; status.Error = smbDetail;
                return;
            }
            AddStep("SMB erişim testi", "done", "Admin share erişimi başarılı");

            // Step 3: Copy files
            AddStep("Dosya kopyalama", "running");
            var (copyOk, copyDetail) = await CopyFiles(ip, zipPath, storeCode, backendUrl);
            if (!copyOk)
            {
                AddStep("Dosya kopyalama", "error", copyDetail);
                status.Phase = "error"; status.Error = copyDetail;
                return;
            }
            AddStep("Dosya kopyalama", "done", "Agent dosyaları kopyalandı");

            // Step 4: .NET 8 Runtime kontrolü — yoksa otomatik kur (tools/dotnet-runtime-8.exe)
            AddStep(".NET 8 Runtime kontrolü", "running");
            var (netOk, netDetail) = await CheckDotNetRuntime(ip);
            if (!netOk)
            {
                AddStep(".NET 8 Runtime kontrolü", "running", "Runtime yok, otomatik kurulum baslatiliyor...");
                var (instOk, instDetail) = await InstallDotNetRuntime(ip);
                if (!instOk)
                {
                    AddStep(".NET 8 Runtime kontrolü", "error", instDetail);
                    status.Phase = "error"; status.Error = instDetail;
                    return;
                }
                // Re-check after install
                (netOk, netDetail) = await CheckDotNetRuntime(ip);
                if (!netOk)
                {
                    AddStep(".NET 8 Runtime kontrolü", "error", "Kurulum tamamlandi ama runtime dogrulanamadi: " + netDetail);
                    status.Phase = "error"; status.Error = netDetail;
                    return;
                }
                netDetail = "Otomatik kuruldu — " + netDetail;
            }
            AddStep(".NET 8 Runtime kontrolü", "done", netDetail);

            // Step 5: Install & start service via sc.exe
            AddStep("Servis kurulumu", "running");
            var (svcState, svcDetail) = await InstallAndStartService(ip);
            AddStep("Servis kurulumu", svcState, svcDetail);
            if (svcState == "error")
            {
                status.Phase = "error"; status.Error = svcDetail;
                return;
            }
            var hasWarnings = svcState == "warn";

            // Step 6: VNC kurulumu
            AddStep("VNC kurulumu", "running");
            var (vncState, vncDetail) = await InstallVnc(ip);
            AddStep("VNC kurulumu", vncState, vncDetail);
            if (vncState == "warn" || vncState == "error")
            {
                hasWarnings = true;
                _logger.LogWarning("[RemoteInstall] VNC kurulumu {State} ({Ip}): {Detail}", vncState, ip, vncDetail);
            }

            // Step 7: Agent heartbeat dogrulama — DB'ye kayit atip atmadigini kontrol et
            AddStep("Agent heartbeat kontrolü", "running");
            var (hbState, hbDetail) = await VerifyAgentHeartbeat(ip, status.StartedAt);
            AddStep("Agent heartbeat kontrolü", hbState, hbDetail);
            if (hbState == "warn" || hbState == "error") hasWarnings = true;

            status.Phase = hasWarnings ? "warn" : "done";
            status.CompletedAt = DateTime.UtcNow;
            _logger.LogInformation("[RemoteInstall] Tamamlandı: {Ip} (Mağaza: {Store}) — phase={Phase}", ip, storeCode, status.Phase);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[RemoteInstall] Hata: {Ip}", ip);
            status.Phase = "error";
            status.Error = ex.Message;
            AddStep("Hata", "error", ex.Message);
        }
    }

    private async Task<bool> TestPing(string ip)
    {
        try
        {
            using var ping = new System.Net.NetworkInformation.Ping();
            var reply = await ping.SendPingAsync(ip, 3000);
            return reply.Status == System.Net.NetworkInformation.IPStatus.Success;
        }
        catch { return false; }
    }

    private async Task<(bool Ok, string Detail)> TestSmb(string ip)
    {
        // Script dosyası üzerinden çalıştır — bash UNC path escaping sorununu önler
        var scriptPath = Path.Combine(Path.GetTempPath(), $"mudosmb_{ip.Replace('.', '_')}.ps1");
        var script = @"
$ErrorActionPreference = 'Stop'
$ip = '" + ip + @"'
try {
    $items = Get-ChildItem ""\\$ip\C$"" -ErrorAction Stop | Select-Object -First 1
    Write-Output 'SMB_OK'
} catch {
    Write-Output ""SMB_FAIL: $($_.Exception.Message)""
}
";
        await System.IO.File.WriteAllTextAsync(scriptPath, script);
        try
        {
            var (_, output) = await RunPsFile(scriptPath);
            _logger.LogInformation("[RemoteInstall] SMB test {Ip}: {Output}", ip, output);
            if (output.Contains("SMB_OK"))
                return (true, "OK");
            return (false, output.Replace("SMB_FAIL: ", ""));
        }
        finally
        {
            System.IO.File.Delete(scriptPath);
        }
    }

    private async Task<(bool Ok, string Detail)> CopyFiles(string ip, string zipPath, string storeCode, string backendUrl)
    {
        var configObj = BuildAgentConfig(storeCode, backendUrl, ip);
        var configJson = JsonSerializer.Serialize(configObj, new JsonSerializerOptions { WriteIndented = true });

        // Write config JSON to a temp file to avoid escaping issues in PS script
        var configPath = Path.Combine(Path.GetTempPath(), $"mudoconfig_{ip.Replace('.', '_')}.json");
        await System.IO.File.WriteAllTextAsync(configPath, configJson);

        var scriptPath = Path.Combine(Path.GetTempPath(), $"mudocopy_{ip.Replace('.', '_')}.ps1");
        var script = @"
$ErrorActionPreference = 'Stop'
$ip = '" + ip + @"'
$zipPath = '" + zipPath.Replace("'", "''") + @"'
$configPath = '" + configPath.Replace("'", "''") + @"'
try {
    $remotePath = ""\\$ip\C$\Program Files\MudoSoft\Agent""
    if (!(Test-Path $remotePath)) { New-Item -Path $remotePath -ItemType Directory -Force | Out-Null }

    # C:\temp kullan - $env:TEMP 8.3 kisa path dondurebilir, Copy-Item -Recurse bozuluyor
    if (!(Test-Path 'C:\temp')) { New-Item -Path 'C:\temp' -ItemType Directory -Force | Out-Null }
    $tempDir = 'C:\temp\MudoInstall_' + $ip.Replace('.','_') + '_' + (Get-Random)
    if (Test-Path $tempDir) { Remove-Item $tempDir -Recurse -Force }
    New-Item -Path $tempDir -ItemType Directory -Force | Out-Null

    # ZIP'i once IP'ye ozel temp dizine kopyala, sonra extract et (concurrent lock onleme)
    $zipCopy = Join-Path $tempDir 'agent.zip'
    Copy-Item -Path $zipPath -Destination $zipCopy -Force
    Expand-Archive -Path $zipCopy -DestinationPath $tempDir -Force
    Remove-Item $zipCopy -Force

    # Remove appsettings from ZIP (will write fresh)
    $cf = Join-Path $tempDir 'appsettings.json'
    if (Test-Path $cf) { Remove-Item $cf -Force }

    robocopy $tempDir $remotePath /E /NFL /NDL /NJH /NJS /NC /NS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw ""robocopy failed with exit code $LASTEXITCODE"" }

    # Copy fresh config
    Copy-Item -Path $configPath -Destination (Join-Path $remotePath 'appsettings.json') -Force

    # Copy tools folder (tightvnc.msi etc.)
    $localTools = '" + _toolsPath.Replace("'", "''") + @"'
    $remoteTools = Join-Path $remotePath 'tools'
    if (Test-Path $localTools) {
        if (!(Test-Path $remoteTools)) { New-Item -Path $remoteTools -ItemType Directory -Force | Out-Null }
        robocopy $localTools $remoteTools /E /NFL /NDL /NJH /NJS /NC /NS /NP | Out-Null
    }

    Remove-Item $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    Write-Output 'COPY_OK'
} catch {
    Write-Output ""COPY_FAIL: $($_.Exception.Message)""
}
";
        await System.IO.File.WriteAllTextAsync(scriptPath, script);
        try
        {
            var (_, output) = await RunPsFile(scriptPath);
            _logger.LogInformation("[RemoteInstall] CopyFiles {Ip}: {Output}", ip, output);
            if (output.Contains("COPY_OK"))
                return (true, "OK");
            return (false, output.Replace("COPY_FAIL: ", ""));
        }
        finally
        {
            System.IO.File.Delete(scriptPath);
            try { System.IO.File.Delete(configPath); } catch { }
        }
    }

    private async Task<(string State, string Detail)> InstallAndStartService(string ip)
    {
        // Dosyalar zaten SMB ile kopyalandı.
        // sc.exe argument quoting'i icin cmd /c kullaniyoruz — PowerShell native command
        // arg parser'i binPath= "..." quoting'i bozuyor (exit 1639 / Invalid command line argument).
        var scriptPath = Path.Combine(Path.GetTempPath(), $"mudosvc_{ip.Replace('.', '_')}.ps1");

        var script = @"
$ErrorActionPreference = 'Stop'
$ip = '" + ip + @"'
$svcName = 'MudosoftAgentService'
try {
    # Registry: servis baslangic timeout'unu 120sn yap (yavas makineler icin)
    reg.exe add ""\\$ip\HKLM\SYSTEM\CurrentControlSet\Control"" /v ServicesPipeTimeout /t REG_DWORD /d 120000 /f 2>&1 | Out-Null

    # Mevcut servisi durdur (process yoksa hata yutulur).
    # PS 5.1'de native command stderr'i ErrorRecord'a sarilir ve $ErrorActionPreference='Stop' ile
    # try/catch'i tetikler — bu yuzden bu komutlari Continue ile sariyoruz.
    $prevEAP = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    cmd /c ""sc.exe \\$ip stop $svcName"" *>&1 | Out-Null
    Start-Sleep -Seconds 2
    cmd /c ""taskkill /s $ip /f /im MudoSoft.Agent.exe /t"" *>&1 | Out-Null
    $ErrorActionPreference = $prevEAP

    # Servis var mi kontrol et — service yoksa sc 1060 + stderr basar, EAP'yi gecici dusur
    $prevEAP = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    cmd /c ""sc.exe \\$ip query $svcName"" *>&1 | Out-Null
    $queryExit = $LASTEXITCODE
    $ErrorActionPreference = $prevEAP

    # binPath= ""\""C:\Program Files\...\exe\"" --service"" -> cmd /c ile native quoting devre disi
    $createCmd = 'sc.exe \\' + $ip + ' create ' + $svcName + ' binPath= ""\""C:\Program Files\MudoSoft\Agent\MudoSoft.Agent.exe\"" --service"" start= delayed-auto obj= LocalSystem DisplayName= ""Orchestra Agent Service""'
    $configCmd = 'sc.exe \\' + $ip + ' config ' + $svcName + ' binPath= ""\""C:\Program Files\MudoSoft\Agent\MudoSoft.Agent.exe\"" --service"" start= delayed-auto'

    if ($queryExit -ne 0) {
        $createOutput = cmd /c $createCmd 2>&1
        $createExit = $LASTEXITCODE
        if ($createExit -ne 0) {
            Write-Output ""SVC_FAIL: sc create basarisiz (exit=$createExit): $createOutput""
            return
        }
    } else {
        $configOutput = cmd /c $configCmd 2>&1
        $configExit = $LASTEXITCODE
        if ($configExit -ne 0) {
            Write-Output ""SVC_FAIL: sc config basarisiz (exit=$configExit): $configOutput""
            return
        }
    }

    # DelayedAutoStart, description, failure policy (kritik degil, sessiz gec)
    reg.exe add ""\\$ip\HKLM\SYSTEM\CurrentControlSet\Services\$svcName"" /v DelayedAutoStart /t REG_DWORD /d 1 /f 2>&1 | Out-Null
    cmd /c ""sc.exe \\$ip description $svcName \""MudoSoft RMM Agent\"""" 2>&1 | Out-Null
    cmd /c ""sc.exe \\$ip failure $svcName reset= 86400 actions= restart/5000/restart/10000/restart/30000"" 2>&1 | Out-Null

    # Servisi baslat
    $startOutput = cmd /c ""sc.exe \\$ip start $svcName"" 2>&1
    $startExit = $LASTEXITCODE
    # exit 1056 = ""An instance of the service is already running"" — tamam
    if ($startExit -ne 0 -and $startExit -ne 1056) {
        Write-Output ""SVC_FAIL: sc start basarisiz (exit=$startExit): $startOutput""
        return
    }

    # Servis durumunu 30sn bekle — RUNNING olmali
    for ($i = 0; $i -lt 6; $i++) {
        Start-Sleep -Seconds 5
        $svc = cmd /c ""sc.exe \\$ip query $svcName"" 2>&1
        if ($svc -match 'RUNNING') {
            Write-Output 'SVC_OK: Servis kuruldu ve calisiyor'
            return
        }
    }

    $finalState = (cmd /c ""sc.exe \\$ip query $svcName"" 2>&1) -join ' '
    Write-Output ""SVC_WARN: Servis olusturuldu ama 30sn icinde RUNNING durumuna gelmedi. Son durum: $finalState""
} catch {
    Write-Output ""SVC_FAIL: $($_.Exception.Message)""
}
";
        await System.IO.File.WriteAllTextAsync(scriptPath, script);
        try
        {
            var (_, output) = await RunPsFile(scriptPath);
            _logger.LogInformation("[RemoteInstall] Service {Ip}: {Output}", ip, output);
            if (output.Contains("SVC_OK"))
                return ("done", ExtractMarker(output, "SVC_OK: "));
            if (output.Contains("SVC_WARN"))
                return ("warn", ExtractMarker(output, "SVC_WARN: "));
            return ("error", ExtractMarker(output, "SVC_FAIL: "));
        }
        finally
        {
            System.IO.File.Delete(scriptPath);
        }
    }

    private static string ExtractMarker(string output, string marker)
    {
        var idx = output.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0) return output.Trim();
        return output.Substring(idx + marker.Length).Trim();
    }

    /// <summary>
    /// Run a .ps1 file — avoids bash/cmd UNC path escaping issues
    /// </summary>
    private async Task<(int ExitCode, string Output)> RunPsFile(string scriptPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{scriptPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        var combined = string.IsNullOrWhiteSpace(error) ? output.Trim() : $"{output.Trim()}\n{error.Trim()}";
        return (process.ExitCode, combined);
    }

    /// <summary>
    /// TightVNC kurulumu: MSI kopyala, registry ile şifre ayarla, firewall kuralı ekle, servisi başlat, DB güncelle
    /// </summary>
    private static string GenerateVncPassword()
    {
        const string chars = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(8);
        return new string(bytes.Select(b => chars[b % chars.Length]).ToArray());
    }

    private async Task<(bool Ok, string Detail)> CheckDotNetRuntime(string ip)
    {
        // Remote makinede C:\Program Files\dotnet\shared\Microsoft.NETCore.App altinda 8.x klasoru var mi?
        // Agent self-contained publish edilmiyor → runtime mutlaka yüklü olmalı.
        var scriptPath = Path.Combine(Path.GetTempPath(), $"mudonet_{ip.Replace('.', '_')}.ps1");
        var script = @"
$ErrorActionPreference = 'Stop'
$ip = '" + ip + @"'
try {
    $sharedPath = ""\\$ip\C$\Program Files\dotnet\shared\Microsoft.NETCore.App""
    if (!(Test-Path $sharedPath)) {
        Write-Output 'NET_FAIL: .NET Runtime yuklu degil (Microsoft.NETCore.App dizini bulunamadi)'
        return
    }
    $versions = @(Get-ChildItem $sharedPath -Directory -ErrorAction Stop | Select-Object -ExpandProperty Name)
    $net8 = @($versions | Where-Object { $_ -like '8.*' })
    if ($net8.Count -eq 0) {
        Write-Output ""NET_FAIL: .NET 8 Runtime yuklu degil. Mevcut surumler: $($versions -join ', ')""
        return
    }
    Write-Output ""NET_OK: $($net8 -join ', ')""
} catch {
    Write-Output ""NET_FAIL: $($_.Exception.Message)""
}
";
        await System.IO.File.WriteAllTextAsync(scriptPath, script);
        try
        {
            var (_, output) = await RunPsFile(scriptPath);
            _logger.LogInformation("[RemoteInstall] .NET check {Ip}: {Output}", ip, output);
            if (output.Contains("NET_OK"))
                return (true, ".NET 8 Runtime mevcut: " + ExtractMarker(output, "NET_OK: "));
            return (false, ExtractMarker(output, "NET_FAIL: "));
        }
        finally
        {
            try { System.IO.File.Delete(scriptPath); } catch { }
        }
    }

    private async Task<(bool Ok, string Detail)> InstallDotNetRuntime(string ip)
    {
        // tools/dotnet-runtime-8.exe zaten CopyFiles ile remote'a kopyalandi.
        // WMI Win32_Process.Create ile silent install — ortalama 30-90sn surer.
        var remoteExe = $@"\\{ip}\C$\Program Files\MudoSoft\Agent\tools\dotnet-runtime-8.exe";
        if (!System.IO.File.Exists(remoteExe))
        {
            return (false, $"dotnet-runtime-8.exe remote'da bulunamadi: {remoteExe}");
        }

        var scriptPath = Path.Combine(Path.GetTempPath(), $"mudonetinst_{ip.Replace('.', '_')}.ps1");
        var script = @"
$ErrorActionPreference = 'Stop'
$ip = '" + ip + @"'
try {
    $remoteTemp = ""\\$ip\C$\temp""
    if (!(Test-Path $remoteTemp)) { New-Item -Path $remoteTemp -ItemType Directory -Force | Out-Null }
    Copy-Item -Path ""\\$ip\C$\Program Files\MudoSoft\Agent\tools\dotnet-runtime-8.exe"" -Destination ""$remoteTemp\dotnet-runtime-8.exe"" -Force

    $cmd = 'cmd.exe /c C:\temp\dotnet-runtime-8.exe /install /quiet /norestart > C:\temp\dotnet_install.log 2>&1'
    $r = Invoke-WmiMethod -ComputerName $ip -Class Win32_Process -Name Create -ArgumentList $cmd -ErrorAction Stop
    if ($r.ReturnValue -ne 0) {
        Write-Output ""NETINST_FAIL: WMI process create failed (return=$($r.ReturnValue))""
        return
    }

    # 5 dk bekle, .NET 8 directory'sini bul
    for ($i = 0; $i -lt 60; $i++) {
        Start-Sleep -Seconds 5
        $sharedPath = ""\\$ip\C$\Program Files\dotnet\shared\Microsoft.NETCore.App""
        if (Test-Path $sharedPath) {
            $vers = @(Get-ChildItem $sharedPath -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like '8.*' })
            if ($vers.Count -gt 0) {
                Write-Output ""NETINST_OK: $($vers[0].Name)""
                return
            }
        }
    }
    Write-Output 'NETINST_TIMEOUT'
} catch {
    Write-Output ""NETINST_FAIL: $($_.Exception.Message)""
}
";
        await System.IO.File.WriteAllTextAsync(scriptPath, script);
        try
        {
            var (_, output) = await RunPsFile(scriptPath);
            _logger.LogInformation("[RemoteInstall] .NET install {Ip}: {Output}", ip, output);
            if (output.Contains("NETINST_OK"))
                return (true, ".NET 8 Runtime kuruldu");
            if (output.Contains("NETINST_TIMEOUT"))
                return (false, ".NET 8 kurulum 5dk icinde tamamlanmadi");
            return (false, ExtractMarker(output, "NETINST_FAIL: "));
        }
        finally
        {
            try { System.IO.File.Delete(scriptPath); } catch { }
        }
    }

    private async Task<(string State, string Detail)> VerifyAgentHeartbeat(string ip, DateTime installStartedAt)
    {
        // Agent servis baslatildiktan sonra backend'e heartbeat atmali.
        // 120sn icinde Devices tablosunda LastSeen > installStartedAt olan bir kayit bekliyoruz.
        const int maxSeconds = 120;
        for (int i = 0; i < maxSeconds / 5; i++)
        {
            await Task.Delay(5000);
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var scopedDb = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();
                var device = await scopedDb.Devices
                    .AsNoTracking()
                    .FirstOrDefaultAsync(d => d.IpAddress == ip);
                if (device != null && device.LastSeen > installStartedAt)
                {
                    var secs = (int)(DateTime.UtcNow - installStartedAt).TotalSeconds;
                    return ("done", $"Agent backend'e bağlandı ({secs}sn sonra, v{device.AgentVersion ?? "?"})");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[RemoteInstall] Heartbeat check DB hatası {Ip}", ip);
            }
        }
        return ("warn", $"Agent {maxSeconds}sn içinde backend'e heartbeat atmadı. Servis kuruldu ama çalışmıyor olabilir — manuel kontrol gerekli.");
    }

    private async Task<(string State, string Detail)> InstallVnc(string ip)
    {
        var vncPassword = GenerateVncPassword();

        // Check if TightVNC already installed
        var tvnCheck = $@"\\{ip}\C$\Program Files\TightVNC\tvnserver.exe";
        bool alreadyInstalled = false;
        try
        {
            var checkScript = Path.Combine(Path.GetTempPath(), $"mudovncchk_{ip.Replace('.', '_')}.ps1");
            await System.IO.File.WriteAllTextAsync(checkScript, $@"
$ip = '{ip}'
if (Test-Path ""\\$ip\C$\Program Files\TightVNC\tvnserver.exe"") {{ Write-Output 'VNC_INSTALLED' }} else {{ Write-Output 'VNC_NOT_INSTALLED' }}
");
            var (_, checkOutput) = await RunPsFile(checkScript);
            try { System.IO.File.Delete(checkScript); } catch { }
            alreadyInstalled = checkOutput.Contains("VNC_INSTALLED");
        }
        catch { }

        // Compute DES-encrypted password for registry
        var encBytes = EncryptVncPassword(vncPassword);
        var hexStr = string.Join("", encBytes.Select(b => b.ToString("x2")));

        string batContent;
        if (!alreadyInstalled)
        {
            // Full install: copy MSI, install, configure, firewall, restart
            // First copy tightvnc.msi from agent's tools directory on remote machine
            var remoteMsiPath = $@"\\{ip}\C$\Program Files\MudoSoft\Agent\tools\tightvnc.msi";
            bool msiExists = false;
            try
            {
                var msiCheckScript = Path.Combine(Path.GetTempPath(), $"mudovncmsi_{ip.Replace('.', '_')}.ps1");
                await System.IO.File.WriteAllTextAsync(msiCheckScript, $@"
$ip = '{ip}'
if (Test-Path ""\\$ip\C$\Program Files\MudoSoft\Agent\tools\tightvnc.msi"") {{ Write-Output 'MSI_OK' }} else {{ Write-Output 'MSI_MISSING' }}
");
                var (_, msiOutput) = await RunPsFile(msiCheckScript);
                try { System.IO.File.Delete(msiCheckScript); } catch { }
                msiExists = msiOutput.Contains("MSI_OK");
            }
            catch { }

            if (!msiExists)
            {
                return ("error", "TightVNC MSI bulunamadı (agent/tools/tightvnc.msi)");
            }

            batContent = "@echo off\r\n"
                + "copy \"C:\\Program Files\\MudoSoft\\Agent\\tools\\tightvnc.msi\" \"C:\\temp\\tightvnc.msi\" /Y >nul\r\n"
                + "msiexec /i \"C:\\temp\\tightvnc.msi\" /quiet /norestart ADDLOCAL=Server SET_USEFIREWALL=1 SET_ACCEPTHTTPCONNECTIONS=0 SET_CONNECTPRIORITY=0\r\n"
                + "timeout /t 5 /nobreak >nul\r\n"
                + $"reg add \"HKLM\\SOFTWARE\\TightVNC\\Server\" /v Password /t REG_BINARY /d {hexStr} /f\r\n"
                + "reg add \"HKLM\\SOFTWARE\\TightVNC\\Server\" /v AcceptConnections /t REG_DWORD /d 1 /f\r\n"
                + "reg add \"HKLM\\SOFTWARE\\TightVNC\\Server\" /v RfbPort /t REG_DWORD /d 5900 /f\r\n"
                + "netsh advfirewall firewall delete rule name=\"TightVNC\" >nul 2>&1\r\n"
                + "netsh advfirewall firewall add rule name=\"TightVNC\" dir=in action=allow protocol=TCP localport=5900\r\n"
                + "net stop tvnserver >nul 2>&1\r\n"
                + "timeout /t 2 /nobreak >nul\r\n"
                + "net start tvnserver\r\n"
                + "del \"C:\\temp\\tightvnc.msi\" >nul 2>&1\r\n"
                + "echo DONE > C:\\temp\\vnc_setup_done.txt\r\n"
                + "del \"%~f0\" >nul 2>&1\r\n";
        }
        else
        {
            // Already installed — just configure password, firewall, restart
            batContent = "@echo off\r\n"
                + $"reg add \"HKLM\\SOFTWARE\\TightVNC\\Server\" /v Password /t REG_BINARY /d {hexStr} /f\r\n"
                + "reg add \"HKLM\\SOFTWARE\\TightVNC\\Server\" /v AcceptConnections /t REG_DWORD /d 1 /f\r\n"
                + "reg add \"HKLM\\SOFTWARE\\TightVNC\\Server\" /v RfbPort /t REG_DWORD /d 5900 /f\r\n"
                + "netsh advfirewall firewall delete rule name=\"TightVNC\" >nul 2>&1\r\n"
                + "netsh advfirewall firewall add rule name=\"TightVNC\" dir=in action=allow protocol=TCP localport=5900\r\n"
                + "net stop tvnserver >nul 2>&1\r\n"
                + "timeout /t 2 /nobreak >nul\r\n"
                + "net start tvnserver\r\n"
                + "echo DONE > C:\\temp\\vnc_setup_done.txt\r\n"
                + "del \"%~f0\" >nul 2>&1\r\n";
        }

        // schtasks yerine WMI Win32_Process.Create ile uzaktan bat calistiriyoruz
        // (schtasks EDR alert'i tetikliyordu — T1053.005 lateral movement)
        var scriptPath = Path.Combine(Path.GetTempPath(), $"mudovnc_{ip.Replace('.', '_')}.ps1");
        var batLocalPath = Path.Combine(Path.GetTempPath(), $"mudovnc_{ip.Replace('.', '_')}.bat");
        await System.IO.File.WriteAllTextAsync(batLocalPath, batContent, System.Text.Encoding.ASCII);

        var psScript = @"
$ErrorActionPreference = 'Stop'
$ip = '" + ip + @"'
$batSource = '" + batLocalPath.Replace("'", "''") + @"'
try {
    $remoteTempDir = ""\\$ip\C$\temp""
    if (!(Test-Path $remoteTempDir)) { New-Item -Path $remoteTempDir -ItemType Directory -Force | Out-Null }
    Copy-Item -Path $batSource -Destination ""$remoteTempDir\vnc_setup.bat"" -Force

    # WMI ile uzaktan bat calistir — schtasks kullanmadan
    $result = Invoke-WmiMethod -ComputerName $ip -Class Win32_Process -Name Create -ArgumentList 'cmd.exe /c C:\temp\vnc_setup.bat' 2>&1
    if ($result.ReturnValue -ne 0) {
        Write-Output ""VNC_FAIL: WMI process create failed (return=$($result.ReturnValue))""
        return
    }

    for ($i = 0; $i -lt 15; $i++) {
        Start-Sleep -Seconds 3
        if (Test-Path ""$remoteTempDir\vnc_setup_done.txt"") {
            Remove-Item ""$remoteTempDir\vnc_setup_done.txt"" -Force -ErrorAction SilentlyContinue
            Write-Output 'VNC_OK'
            return
        }
    }
    Write-Output 'VNC_TIMEOUT'
} catch {
    Write-Output ""VNC_FAIL: $($_.Exception.Message)""
}
";
        await System.IO.File.WriteAllTextAsync(scriptPath, psScript);

        try
        {
            var (_, output) = await RunPsFile(scriptPath);
            _logger.LogInformation("[RemoteInstall] VNC setup {Ip}: {Output}", ip, output);

            if (output.Contains("VNC_OK"))
            {
                // DB'yi yalnizca gercek basarida guncelle
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var scopedDb = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();

                    // Cihaz agent heartbeat atmadan önce DB'de olmayabilir — 60sn bekle
                    Device? device = null;
                    for (int attempt = 0; attempt < 12; attempt++)
                    {
                        device = await scopedDb.Devices.FirstOrDefaultAsync(d => d.IpAddress == ip);
                        if (device != null) break;
                        await Task.Delay(5000);
                    }

                    if (device != null)
                    {
                        device.VncInstalled = true;
                        device.VncPassword = vncPassword;
                        device.VncPort = 5900;
                        await scopedDb.SaveChangesAsync();
                        _logger.LogInformation("[RemoteInstall] VNC DB updated for {Ip}", ip);
                    }
                    else
                    {
                        _logger.LogWarning("[RemoteInstall] VNC kuruldu ama cihaz 60sn içinde DB'ye kayıt olmadı: {Ip}", ip);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[RemoteInstall] VNC DB update failed for {Ip}", ip);
                }

                return ("done", alreadyInstalled ? "VNC şifresi yapılandırıldı" : "TightVNC kuruldu ve yapılandırıldı");
            }
            if (output.Contains("VNC_TIMEOUT"))
            {
                return ("warn", "VNC kurulum komutu gönderildi ama 45sn içinde tamamlandı sinyali alınmadı. MSI sessizce başarısız olmuş olabilir.");
            }
            return ("error", ExtractMarker(output, "VNC_FAIL: "));
        }
        finally
        {
            try { System.IO.File.Delete(scriptPath); } catch { }
            try { System.IO.File.Delete(batLocalPath); } catch { }
        }
    }

    /// <summary>
    /// VNC DES password encryption for TightVNC registry storage.
    /// </summary>
    private static byte[] EncryptVncPassword(string password)
    {
        // TightVNC fixed key (bit-reversed): {0xE8, 0x4A, 0xD6, 0x60, 0xC4, 0x72, 0x1A, 0xE0}
        var fixedKey = new byte[] { 0xE8, 0x4A, 0xD6, 0x60, 0xC4, 0x72, 0x1A, 0xE0 };

        var pwdBytes = new byte[8];
        var passBytes = System.Text.Encoding.ASCII.GetBytes(password);
        Array.Copy(passBytes, pwdBytes, Math.Min(passBytes.Length, 8));

        using var des = System.Security.Cryptography.DES.Create();
        des.Mode = System.Security.Cryptography.CipherMode.ECB;
        des.Padding = System.Security.Cryptography.PaddingMode.None;
        des.Key = fixedKey;

        using var encryptor = des.CreateEncryptor();
        var encrypted = new byte[8];
        encryptor.TransformBlock(pwdBytes, 0, 8, encrypted, 0);
        return encrypted;
    }

    private static object BuildAgentConfig(string storeCode, string backendUrl, string ipAddress)
    {
        return new
        {
            Agent = new
            {
                BackendUrl = backendUrl,
                StoreCode = storeCode,
                HeartbeatIntervalSeconds = 5,
                CommandPollIntervalSeconds = 1,
                IpAddress = ipAddress,
                Collectors = new
                {
                    PortMonitor = new
                    {
                        Enabled = true, IntervalSeconds = 60,
                        Ports = new[] {
                            new { Port = 1433, ServiceName = "SQL Server" },
                            new { Port = 3389, ServiceName = "RDP" }
                        },
                        TimeoutMs = 3000
                    },
                    ProcessUsage = new { Enabled = true, IntervalSeconds = 60, TopCount = 10 },
                    ServiceMonitor = new
                    {
                        Enabled = true, IntervalSeconds = 60,
                        MonitoredServices = new[] { "MSSQL$SQLEXPRESS", "SQLBrowser" },
                        AutoRestart = true, MaxRestartsPerHour = 3
                    },
                    EventLog = new { Enabled = true, IntervalSeconds = 300, LogNames = new[] { "System", "Application" }, MaxEventsPerCycle = 50 },
                    DiskHealth = new { Enabled = true, IntervalSeconds = 3600 },
                    WindowsUpdate = new { Enabled = true, IntervalSeconds = 3600 },
                    Temperature = new { Enabled = true, IntervalSeconds = 60 },
                    UpsStatus = new { Enabled = true, IntervalSeconds = 30 },
                    NetworkSpeed = new { Enabled = true, IntervalSeconds = 3600, TestUrl = "http://speedtest.tele2.net/10MB.zip", TimeoutSeconds = 30 },
                    UptimeReport = new { Enabled = true, IntervalSeconds = 600 },
                    ScheduledCleanup = new
                    {
                        Enabled = true, IntervalSeconds = 86400,
                        Targets = new[] {
                            new { Path = "%TEMP%", MaxAgeDays = 7 },
                            new { Path = @"C:\Windows\Prefetch", MaxAgeDays = 30 },
                            new { Path = @"C:\Windows\SoftwareDistribution\Download", MaxAgeDays = 7 }
                        }
                    }
                }
            }
        };
    }
}

public class RemoteInstallRequest
{
    public string IpAddress { get; set; } = "";
    public string? StoreCode { get; set; }
}

public class InstallStatus
{
    public string Id { get; set; } = "";
    public string IpAddress { get; set; } = "";
    public string StoreCode { get; set; } = "";
    public string Phase { get; set; } = "pending";
    public string? Error { get; set; }
    public List<InstallStep> Steps { get; set; } = new();
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class InstallStep
{
    public string Name { get; set; } = "";
    public string State { get; set; } = "pending";
    public string Detail { get; set; } = "";
}

public class SubnetScanRequest
{
    public string Subnet { get; set; } = "";
    public string? DeviceId { get; set; }
    public int? StartIp { get; set; }
    public int? EndIp { get; set; }
    public bool EnrichOnly { get; set; }
    public List<RawScanDevice>? RawDevices { get; set; }
}

public class RawScanDevice
{
    public string IpAddress { get; set; } = "";
    public string? MacAddress { get; set; }
    public string? Hostname { get; set; }
    public int PingMs { get; set; }
}

public class AgentScanItem
{
    public string Ip { get; set; } = "";
    public string? Hostname { get; set; }
    public string? Mac { get; set; }
    public int PingMs { get; set; }
}

public class SubnetScanResult
{
    public string IpAddress { get; set; } = "";
    public bool Reachable { get; set; }
    public string? Hostname { get; set; }
    public string? MacAddress { get; set; }
    public string? Vendor { get; set; }
    public bool HasAgent { get; set; }
    public string? AgentVersion { get; set; }
    public bool Online { get; set; }
    public int StoreCode { get; set; }
    public string? DeviceType { get; set; }
    public int PingMs { get; set; }
}
