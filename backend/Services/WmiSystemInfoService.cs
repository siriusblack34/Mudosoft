using System.Management;
using Microsoft.Extensions.Configuration;
using MudoSoft.Backend.Models;

namespace MudoSoft.Backend.Services
{
    public class WmiSystemInfoService : IWmiSystemInfoService
    {
        private readonly string _domain;
        private readonly string _username;
        private readonly string? _password;
        private readonly int _timeout;

        public WmiSystemInfoService(IConfiguration config)
        {
            var wmi = config.GetSection("MudoSoft:Wmi");

            _domain = wmi["Domain"] ?? "";
            _username = wmi["Username"] ?? "";

            var secretKey = wmi["PasswordSecretKey"];
            _password = !string.IsNullOrWhiteSpace(secretKey)
                ? Environment.GetEnvironmentVariable(secretKey, EnvironmentVariableTarget.Machine)
                : null;

            _timeout = int.TryParse(wmi["Timeout"], out var t) ? t : 5000;
        }

        public async Task<WmiSystemInfo?> GetSystemInfo(string ip)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var co = new ConnectionOptions
                    {
                        Username = $"{_domain}\\{_username}",
                        Password = _password,
                        Timeout = TimeSpan.FromMilliseconds(_timeout),
                        EnablePrivileges = true
                    };

                    var scope = new ManagementScope($"\\\\{ip}\\root\\cimv2", co);
                    scope.Connect();

                    // OS Name
                    string? os = ExecQuery(scope, "SELECT Caption FROM Win32_OperatingSystem",
                        m => m["Caption"]?.ToString());

                    // CPU %
                    double? cpu = ExecQuery(scope, "SELECT LoadPercentage FROM Win32_Processor",
                        m => Convert.ToDouble(m["LoadPercentage"]));

                    // RAM %
                    double? ram = ExecQuery(scope, "SELECT FreePhysicalMemory, TotalVisibleMemorySize FROM Win32_OperatingSystem",
                        m =>
                        {
                            double free = Convert.ToDouble(m["FreePhysicalMemory"]);
                            double total = Convert.ToDouble(m["TotalVisibleMemorySize"]);
                            return Math.Round((1 - free / total) * 100, 2);
                        });

                    // Disk %
                    double? disk = ExecQuery(scope, "SELECT FreeSpace, Size FROM Win32_LogicalDisk WHERE DeviceID='C:'",
                        m =>
                        {
                            double free = Convert.ToDouble(m["FreeSpace"]);
                            double size = Convert.ToDouble(m["Size"]);
                            return Math.Round((1 - free / size) * 100, 2);
                        });

                    return new WmiSystemInfo
                    {
                        Os = os,
                        CpuUsage = cpu,
                        RamUsage = ram,
                        DiskUsage = disk,
                        SqlVersion = null,
                        PosVersion = null
                    };
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ WMI ERROR on {ip}: {ex.Message}");
                    return null;
                }
            });
        }

        private static T? ExecQuery<T>(ManagementScope scope, string query, Func<ManagementObject, T?> selector)
        {
            try
            {
                var searcher = new ManagementObjectSearcher(scope, new ObjectQuery(query));
                var result = searcher.Get().Cast<ManagementObject>().FirstOrDefault();
                return result is null ? default : selector(result);
            }
            catch
            {
                return default;
            }
        }
    }
}
