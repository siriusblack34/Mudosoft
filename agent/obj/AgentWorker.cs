using System.Diagnostics;
using System.Management;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace MudoSoft.Agent;

public class AgentWorker : BackgroundService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private static readonly TimeSpan ReportInterval = TimeSpan.FromSeconds(60);

    public AgentWorker(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var report = CollectAgentData();
            await SendReport(report, stoppingToken);

            await Task.Delay(ReportInterval, stoppingToken);
        }
    }

    private AgentReport CollectAgentData()
    {
        return new AgentReport
        {
            Ip = GetLocalIp(),
            Hostname = Environment.MachineName,
            Cpu = GetCpuUsage(),
            Ram = GetRamUsage(),
            Disk = GetDiskUsage(),
            SqlVersion = GetSqlVersion(),
            PosVersion = GetPosVersion(),
            AgentVersion = "1.0.0",
            Timestamp = DateTime.UtcNow
        };
    }

    private async Task SendReport(AgentReport report, CancellationToken ct)
    {
        var http = _httpClientFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(5);

        var json = JsonSerializer.Serialize(report);

        try
        {
            await http.PostAsync(
                "http://YOUR_BACKEND_SERVER/api/agent/report",
                new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
                ct);
        }
        catch { /* swallow errors */ }
    }

    private static string GetLocalIp()
    {
        return System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName())
            .AddressList
            .FirstOrDefault(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?
            .ToString() ?? "0.0.0.0";
    }

    private static int GetCpuUsage()
    {
        using var cpu = new PerformanceCounter("Processor", "% Processor Time", "_Total");
        cpu.NextValue();
        Thread.Sleep(500);
        return (int)Math.Round(cpu.NextValue());
    }

    private static int GetRamUsage()
    {
        using var ram = new PerformanceCounter("Memory", "% Committed Bytes In Use");
        return (int)Math.Round(ram.NextValue());
    }

    private static int GetDiskUsage()
    {
        DriveInfo drive = new("C");
        var used = drive.TotalSize - drive.AvailableFreeSpace;
        return (int)((used / (double)drive.TotalSize) * 100);
    }

    private static string? GetSqlVersion()
    {
        try
        {
            ProcessStartInfo psi = new()
            {
                FileName = "sqlcmd.exe",
                Arguments = "-Q \"SELECT @@VERSION\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var p = Process.Start(psi);
            string output = p!.StandardOutput.ReadToEnd();
            return output?.Trim();
        }
        catch { return null; }
    }

    private static string? GetPosVersion()
    {
        var file = @"C:\GeniusPOS\parameters.bat";
        if (!File.Exists(file))
            return null;

        var text = File.ReadAllText(file);
        var match = Regex.Match(text, @"set FASHION_JAR=(?<ver>.+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["ver"].Value.Trim() : null;
    }
}

public class AgentReport
{
    public string Ip { get; set; } = "";
    public string Hostname { get; set; } = "";
    public int Cpu { get; set; }
    public int Ram { get; set; }
    public int Disk { get; set; }
    public string? SqlVersion { get; set; }
    public string? PosVersion { get; set; }
    public string AgentVersion { get; set; } = "";
    public DateTime Timestamp { get; set; }
}
