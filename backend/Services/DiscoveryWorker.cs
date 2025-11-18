using MudoSoft.Backend.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MudoSoft.Backend.Services
{
    public class DiscoveryWorker : BackgroundService
    {
        private readonly IDeviceRepository _repo;
        private readonly ILogger<DiscoveryWorker> _log;

        private const int MaxParallelStores = 20;
        private static readonly TimeSpan DiscoveryInterval = TimeSpan.FromMinutes(2);

        public DiscoveryWorker(IDeviceRepository repo, ILogger<DiscoveryWorker> log)
        {
            _repo = repo;
            _log = log;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _log.LogInformation("DiscoveryWorker starting");

            while (!stoppingToken.IsCancellationRequested)
            {
                var started = DateTime.UtcNow;

                try
                {
                    await RunDiscovery(stoppingToken);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Discovery cycle crashed");
                }

                var elapsed = DateTime.UtcNow - started;
                var delay = DiscoveryInterval - elapsed;
                if (delay < TimeSpan.FromSeconds(10))
                    delay = TimeSpan.FromSeconds(10);

                try { await Task.Delay(delay, stoppingToken); }
                catch { break; }
            }

            _log.LogInformation("DiscoveryWorker stopped");
        }

        private async Task RunDiscovery(CancellationToken ct)
        {
            _log.LogInformation("Discovery started. Store count: {StoreCount}", StoreList.Length);

            await Parallel.ForEachAsync(
                StoreList, 
                new ParallelOptions { MaxDegreeOfParallelism = MaxParallelStores, CancellationToken = ct },
                async (storeCode, token) =>
                {
                    try { await ScanStore(storeCode, token); }
                    catch (Exception ex)
                    {
                        _log.LogError(ex, "Discovery failed for store {StoreCode}", storeCode);
                    }
                });

            _log.LogInformation("Discovery finished");
        }

        private async Task ScanStore(int storeCode, CancellationToken ct)
        {
            var targets = new List<(string ip, DeviceType type)>
            {
                ($"192.168.{storeCode}.2",  DeviceType.PC),
                ($"192.168.{storeCode}.31", DeviceType.POS),
                ($"192.168.{storeCode}.32", DeviceType.POS),
                ($"192.168.{storeCode}.33", DeviceType.POS)
            };

            foreach (var (ip, type) in targets)
            {
                if (ct.IsCancellationRequested)
                    break;

                bool reachable = await PingHost(ip, ct);
                if (!reachable)
                    reachable = await CheckTcpPort(ip, 445, ct);

                _log.LogDebug("Store {StoreCode} - {Ip} reachable={Reachable}", storeCode, ip, reachable);

                try
                {
                    var devices = _repo.GetAll();
                    var existing = devices.FirstOrDefault(d => d.IpAddress == ip);

                    if (existing == null)
                    {
                        devices.Add(new Device
                        {
                            Id = $"auto-{storeCode}-{ip.Replace(".", "-")}",
                            StoreCode = storeCode,
                            IpAddress = ip,
                            Type = type,
                            Online = reachable,
                            LastSeen = reachable ? DateTime.UtcNow : null
                        });
                    }
                    else
                    {
                        existing.Online = reachable;
                        existing.LastSeen = reachable ? DateTime.UtcNow : existing.LastSeen;
                        existing.Type = type;
                        existing.StoreCode = storeCode;
                        existing.IpAddress = ip;
                    }

                    _repo.SaveAll(devices);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Device JSON update failed for {Ip}", ip);
                }
            }
        }

        private async Task<bool> PingHost(string ip, CancellationToken ct)
        {
            using var ping = new Ping();
            try
            {
                var reply = await ping.SendPingAsync(ip, 800);
                return reply.Status == IPStatus.Success;
            }
            catch { return false; }
        }

        private async Task<bool> CheckTcpPort(string ip, int port, CancellationToken ct)
        {
            try
            {
                using var client = new TcpClient();
                var connectTask = client.ConnectAsync(ip, port);
                var timeout = Task.Delay(1000, ct);
                var completed = await Task.WhenAny(connectTask, timeout);
                return completed == connectTask && client.Connected;
            }
            catch { return false; }
        }

        private static readonly int[] StoreList = { 146 };

    }
}
