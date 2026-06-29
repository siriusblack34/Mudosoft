using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Data;
using Orchestra.Backend.Services;
using Orchestra.Shared.Dtos;
using Orchestra.Shared.Enums;
using System.Text.Json;

namespace Orchestra.Backend.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/mobile")]
public class MobileBarcodeController : ControllerBase
{
    private readonly OrchestraDbContext _dbContext;
    private readonly CommandQueue _queue;
    private readonly ILogger<MobileBarcodeController> _logger;

    public MobileBarcodeController(OrchestraDbContext dbContext, CommandQueue queue, ILogger<MobileBarcodeController> logger)
    {
        _dbContext = dbContext;
        _queue = queue;
        _logger = logger;
    }

    /// <summary>
    /// TC21 el terminali uygulamasından barkod + adet listesi alır,
    /// ilgili mağaza PC'sine BarcodeExcelExport komutu kuyruğa ekler.
    /// POST /api/mobile/barcode-export
    /// Body: { "storeCode": 5, "items": [{ "barcode": "1234567890", "quantity": 3 }] }
    /// </summary>
    [HttpPost("barcode-export")]
    public async Task<IActionResult> BarcodeExport([FromBody] BarcodeExportRequest request)
    {
        if (request == null)
            return BadRequest(new { error = "Request body gerekli." });

        if (request.Items == null || request.Items.Count == 0)
            return BadRequest(new { error = "En az bir barkod gerekli." });

        if (request.StoreCode <= 0)
            return BadRequest(new { error = "Geçerli bir mağaza kodu (storeCode) gerekli." });

        var device = await _dbContext.Devices
            .Where(d => d.StoreCode == request.StoreCode && d.Online)
            .FirstOrDefaultAsync();

        if (device == null)
            return NotFound(new { error = $"Mağaza {request.StoreCode} için çevrimiçi bir cihaz bulunamadı. Agent çalışıyor mu?" });

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var fileName = $"DataTransfer_{request.StoreCode}_{timestamp}.xlsx";

        var payload = JsonSerializer.Serialize(new
        {
            items = request.Items.Select(i => new { barcode = i.Barcode, quantity = i.Quantity }),
            exportPath = @"C:\Users\Public\Desktop",
            fileName
        });

        var commandId = Guid.NewGuid();
        _queue.Enqueue(new CommandDto
        {
            Id = commandId,
            DeviceId = device.Id,
            Type = CommandType.BarcodeExcelExport,
            Payload = payload,
            CreatedAtUtc = DateTime.UtcNow
        });

        _logger.LogInformation(
            "[Mobile] BarcodeExport kuyruğa eklendi — Mağaza={StoreCode} Cihaz={DeviceId} Komut={CommandId} ÜrünSayısı={Count}",
            request.StoreCode, device.Id, commandId, request.Items.Count);

        return Ok(new
        {
            commandId,
            deviceId = device.Id,
            fileName,
            message = $"{request.Items.Count} barkod alındı. '{fileName}' hazırlanıyor, C:\\ dizinine kaydedilecek."
        });
    }
}

public class BarcodeExportRequest
{
    public int StoreCode { get; set; }
    public List<BarcodeItemRequest> Items { get; set; } = new();
}

public class BarcodeItemRequest
{
    public string Barcode { get; set; } = string.Empty;
    public int Quantity { get; set; }
}
