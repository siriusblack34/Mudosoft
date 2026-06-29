using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Data;
using Orchestra.Backend.Services;

namespace Orchestra.Backend.Controllers;

[ApiController]
[Authorize]
[Route("api/yazicilar")]
public class YazicilarController : ControllerBase
{
    private readonly OrchestraDbContext _db;

    public YazicilarController(OrchestraDbContext db) => _db = db;

    /// <summary>
    /// Offline yazıcıları ve bağlı kasanın yazıcı sicil no'sunu döner.
    /// Sicil numarası kasanın PrinterSerialNumber alanından alınır.
    /// </summary>
    [HttpGet("offline-report")]
    public async Task<IActionResult> GetOfflineReport()
    {
        var cached = DeviceStatusWorker.GetCachedDevices();
        if (cached == null) return Ok(new List<object>());

        var offlinePrinters = cached
            .Where(d => d.DeviceType.StartsWith("Yazici-", StringComparison.OrdinalIgnoreCase)
                     && !d.IsOnline
                     && !d.IsTemporarilyClosed)
            .ToList();

        if (offlinePrinters.Count == 0) return Ok(new List<object>());

        // Yazici-N → K{N} eşleşmesini toplu çek
        var kasaIds = offlinePrinters
            .Select(p =>
            {
                var dash = p.DeviceType.LastIndexOf('-');
                return int.TryParse(p.DeviceType.AsSpan(dash + 1), out var n)
                    ? (id: $"{p.StoreCode}-K{n}", num: n, printer: p)
                    : (id: (string?)null, num: 0, printer: p);
            })
            .Where(x => x.id != null)
            .ToList();

        var kasaSerials = await _db.StoreDevices
            .Where(d => kasaIds.Select(x => x.id!).Contains(d.DeviceId))
            .ToDictionaryAsync(d => d.DeviceId, d => d.PrinterSerialNumber);

        var result = kasaIds
            .Select(x => new
            {
                x.printer.StoreCode,
                x.printer.StoreName,
                KasaNo = x.num,
                PrinterIp = x.printer.CalculatedIpAddress,
                PrinterSerialNumber = kasaSerials.TryGetValue(x.id!, out var sn) ? sn : null,
                x.printer.LastSeen,
            })
            .OrderBy(r => r.StoreCode)
            .ThenBy(r => r.KasaNo)
            .ToList();

        return Ok(result);
    }
}
