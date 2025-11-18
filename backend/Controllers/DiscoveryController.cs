using Microsoft.AspNetCore.Mvc;
using System.Net.NetworkInformation;

namespace MudoSoft.Backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DiscoveryController : ControllerBase
    {
        [HttpPost("store")]
        public async Task<IActionResult> ScanStore([FromBody] StoreScanRequest req)
        {
            if (req.StoreCode <= 0)
                return BadRequest("StoreCode required");

            var result = await ScanStoreDevices(req.StoreCode);
            return Ok(result);
        }

        private async Task<StoreScanResult> ScanStoreDevices(int storeCode)
        {
            var targets = new List<DeviceTarget>
            {
                new($"192.168.{storeCode}.2",   "SERVER"),
                new($"192.168.{storeCode}.31",  "POS1"),
                new($"192.168.{storeCode}.32",  "POS2"),
                new($"192.168.{storeCode}.33",  "POS3")
            };

            var online = new List<DevicePingResult>();
            var offline = new List<DevicePingResult>();

            foreach (var t in targets)
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(t.Ip, 250);

                if (reply.Status == IPStatus.Success)
                    online.Add(new DevicePingResult(t.Ip, t.Type));
                else
                    offline.Add(new DevicePingResult(t.Ip, t.Type));
            }

            return new StoreScanResult(storeCode, online, offline);
        }
    }

    public record StoreScanRequest(int StoreCode);

    public record DeviceTarget(string Ip, string Type);

    public record DevicePingResult(string Ip, string Type);

    public record StoreScanResult(int StoreCode,
        List<DevicePingResult> OnlineDevices,
        List<DevicePingResult> OfflineDevices);
}
