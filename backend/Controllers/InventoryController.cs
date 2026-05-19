using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Data;
using Orchestra.Backend.Services;
using System.Security.Claims;

namespace Orchestra.Backend.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/inventory")]
    public class InventoryController : ControllerBase
    {
        private readonly OrchestraDbContext _db;
        private readonly InventoryImportService _importer;
        private readonly ILogger<InventoryController> _log;
        private readonly ActivityLogService _activity;

        // 50 MB upload limiti
        private const long MaxUploadBytes = 50L * 1024 * 1024;

        public InventoryController(
            OrchestraDbContext db,
            InventoryImportService importer,
            ILogger<InventoryController> log,
            ActivityLogService activity)
        {
            _db = db;
            _importer = importer;
            _log = log;
            _activity = activity;
        }

        /// <summary>
        /// SDP'den export edilen XLSX dosyasini yukler ve envanter tablosuna upsert eder.
        /// </summary>
        [HttpPost("import")]
        [RequestSizeLimit(MaxUploadBytes)]
        [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
        public async Task<ActionResult<InventoryImportResult>> Import(IFormFile file, CancellationToken ct)
        {
            if (file is null || file.Length == 0)
                return BadRequest(new { error = "Dosya bos." });

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (ext != ".xlsx")
                return BadRequest(new { error = "Sadece .xlsx kabul edilir." });

            if (file.Length > MaxUploadBytes)
                return BadRequest(new { error = $"Dosya {MaxUploadBytes / (1024 * 1024)} MB sinirini asiyor." });

            var username = User.FindFirstValue(ClaimTypes.Name) ?? User.Identity?.Name;

            try
            {
                using var stream = file.OpenReadStream();
                var result = await _importer.ImportAsync(stream, file.FileName, file.Length, username, ct);
                await _activity.LogAsync("Inventory", "Import", file.FileName,
                    $"{result.TotalRows} satir; {result.InsertedCount} eklendi, {result.UpdatedCount} guncellendi, {result.UnmatchedStoreCount} eslesmedi", ct: ct);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Inventory import failed");
                await _activity.LogAsync("Inventory", "Import", file.FileName, null, false, ex.Message, ct);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Import gecmisi (son 50).
        /// </summary>
        [HttpGet("batches")]
        public async Task<IActionResult> GetBatches(CancellationToken ct)
        {
            var batches = await _db.InventoryImportBatches
                .OrderByDescending(b => b.ImportedAt)
                .Take(50)
                .ToListAsync(ct);
            return Ok(batches);
        }

        /// <summary>
        /// Sayfali / filtreli envanter listesi.
        /// </summary>
        [HttpGet("assets")]
        public async Task<IActionResult> GetAssets(
            [FromQuery] string? search,
            [FromQuery] int? storeCode,
            [FromQuery] string? productType,
            [FromQuery] string? state,
            [FromQuery] string? fizikselDurum,
            [FromQuery] bool unmatchedOnly = false,
            [FromQuery] string sortBy = "assetName",
            [FromQuery] string sortDir = "asc",
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            CancellationToken ct = default)
        {
            if (page < 1) page = 1;
            if (pageSize < 1 || pageSize > 500) pageSize = 50;

            var q = _db.InventoryAssets.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                var like = $"%{s}%";
                q = q.Where(a =>
                    EF.Functions.ILike(a.AssetName, like) ||
                    (a.Product != null && EF.Functions.ILike(a.Product, like)) ||
                    (a.OrgSerialNumber != null && EF.Functions.ILike(a.OrgSerialNumber, like)) ||
                    (a.ComputerName != null && EF.Functions.ILike(a.ComputerName, like)) ||
                    (a.ProductCode != null && EF.Functions.ILike(a.ProductCode, like)));
            }

            if (storeCode.HasValue)
                q = q.Where(a => a.StoreCode == storeCode.Value);

            if (!string.IsNullOrWhiteSpace(productType))
                q = q.Where(a => a.ProductType == productType);

            if (!string.IsNullOrWhiteSpace(state))
                q = q.Where(a => a.AssetState == state);

            if (!string.IsNullOrWhiteSpace(fizikselDurum))
                q = q.Where(a => a.FizikselDurum == fizikselDurum);

            if (unmatchedOnly)
                q = q.Where(a => a.StoreCode == null);

            // Siralama
            var desc = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
            q = (sortBy?.ToLowerInvariant()) switch
            {
                "storecode" => desc ? q.OrderByDescending(a => a.StoreCode) : q.OrderBy(a => a.StoreCode),
                "producttype" => desc ? q.OrderByDescending(a => a.ProductType) : q.OrderBy(a => a.ProductType),
                "product" => desc ? q.OrderByDescending(a => a.Product) : q.OrderBy(a => a.Product),
                "acquisitiondate" => desc ? q.OrderByDescending(a => a.AcquisitionDate) : q.OrderBy(a => a.AcquisitionDate),
                "expirydate" => desc ? q.OrderByDescending(a => a.ExpiryDate) : q.OrderBy(a => a.ExpiryDate),
                "purchasecost" => desc ? q.OrderByDescending(a => a.PurchaseCost) : q.OrderBy(a => a.PurchaseCost),
                "importedat" => desc ? q.OrderByDescending(a => a.ImportedAt) : q.OrderBy(a => a.ImportedAt),
                _ => desc ? q.OrderByDescending(a => a.AssetName) : q.OrderBy(a => a.AssetName),
            };

            var total = await q.CountAsync(ct);
            var items = await q
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);

            return Ok(new { items, total, page, pageSize });
        }

        /// <summary>
        /// Tek asset detayi.
        /// </summary>
        [HttpGet("assets/{id:int}")]
        public async Task<IActionResult> GetAsset(int id, CancellationToken ct)
        {
            var a = await _db.InventoryAssets.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id, ct);
            return a is null ? NotFound() : Ok(a);
        }

        /// <summary>
        /// Dashboard icin istatistikler — tip dagilimi, durum, magaza top 20, yillik acquisition trendi.
        /// </summary>
        [HttpGet("stats")]
        public async Task<IActionResult> GetStats(CancellationToken ct)
        {
            var byProductType = await _db.InventoryAssets
                .GroupBy(a => a.ProductType ?? "(Bos)")
                .Select(g => new { name = g.Key, count = g.Count() })
                .OrderByDescending(x => x.count)
                .ToListAsync(ct);

            var byState = await _db.InventoryAssets
                .GroupBy(a => a.AssetState ?? "(Bos)")
                .Select(g => new { name = g.Key, count = g.Count() })
                .OrderByDescending(x => x.count)
                .ToListAsync(ct);

            var byFizikselDurum = await _db.InventoryAssets
                .GroupBy(a => a.FizikselDurum ?? "(Bos)")
                .Select(g => new { name = g.Key, count = g.Count() })
                .OrderByDescending(x => x.count)
                .ToListAsync(ct);

            var byStoreTop20 = await _db.InventoryAssets
                .Where(a => a.StoreCode != null)
                .GroupBy(a => a.StoreCode!.Value)
                .Select(g => new { storeCode = g.Key, count = g.Count() })
                .OrderByDescending(x => x.count)
                .Take(20)
                .ToListAsync(ct);

            var acquisitionByYear = await _db.InventoryAssets
                .Where(a => a.AcquisitionDate != null)
                .GroupBy(a => a.AcquisitionDate!.Value.Year)
                .Select(g => new { year = g.Key, count = g.Count() })
                .OrderBy(x => x.year)
                .ToListAsync(ct);

            var totalCost = await _db.InventoryAssets
                .Where(a => a.PurchaseCost != null)
                .SumAsync(a => a.PurchaseCost ?? 0m, ct);

            return Ok(new
            {
                byProductType,
                byState,
                byFizikselDurum,
                byStoreTop20,
                acquisitionByYear,
                totalPurchaseCost = totalCost,
            });
        }

        /// <summary>
        /// Dropdown'lari beslemek icin distinct degerler.
        /// </summary>
        [HttpGet("filter-options")]
        public async Task<IActionResult> GetFilterOptions(CancellationToken ct)
        {
            var productTypes = await _db.InventoryAssets
                .Where(a => a.ProductType != null)
                .Select(a => a.ProductType!)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync(ct);

            var states = await _db.InventoryAssets
                .Where(a => a.AssetState != null)
                .Select(a => a.AssetState!)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync(ct);

            var fizikselDurumlar = await _db.InventoryAssets
                .Where(a => a.FizikselDurum != null)
                .Select(a => a.FizikselDurum!)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync(ct);

            var stores = await _db.InventoryAssets
                .Where(a => a.StoreCode != null)
                .GroupBy(a => a.StoreCode!.Value)
                .Select(g => new
                {
                    storeCode = g.Key,
                    storeName = g.Select(x => x.StoreNameRaw).FirstOrDefault(),
                    count = g.Count(),
                })
                .OrderBy(x => x.storeCode)
                .ToListAsync(ct);

            return Ok(new { productTypes, states, fizikselDurumlar, stores });
        }

        /// <summary>
        /// StoreDevices'tan tum magazalarin (kod, ad) listesi — mapping dropdown'i icin.
        /// </summary>
        [HttpGet("all-stores")]
        public async Task<IActionResult> GetAllStores(CancellationToken ct)
        {
            var stores = await _db.StoreDevices
                .AsNoTracking()
                .Where(s => s.StoreCode > 0 && !string.IsNullOrEmpty(s.StoreName))
                .GroupBy(s => s.StoreCode)
                .Select(g => new { storeCode = g.Key, storeName = g.Select(x => x.StoreName).FirstOrDefault() })
                .OrderBy(x => x.storeCode)
                .ToListAsync(ct);
            return Ok(stores);
        }

        /// <summary>
        /// Eslesmemis magaza adlari (StoreNameMapping kaydinda StoreCode null).
        /// </summary>
        [HttpGet("unmapped-stores")]
        public async Task<IActionResult> GetUnmappedStores(CancellationToken ct)
        {
            var list = await _db.StoreNameMappings
                .Where(m => m.StoreCode == null)
                .OrderBy(m => m.RawName)
                .ToListAsync(ct);
            return Ok(list);
        }

        public class StoreMappingUpdateDto
        {
            public int? StoreCode { get; set; }
        }

        /// <summary>
        /// Eslesmemis mapping kayitlarini guncel matcher mantigi ile yeniden eslestirir.
        /// </summary>
        [HttpPost("rematch-unmapped")]
        public async Task<IActionResult> RematchUnmapped(CancellationToken ct)
        {
            var (updatedMappings, updatedAssets) = await _importer.RematchUnmappedAsync(ct);
            await _activity.LogAsync("Inventory", "RematchUnmapped", null,
                $"{updatedMappings} mapping eslesti, {updatedAssets} asset guncellendi", ct: ct);
            return Ok(new { updatedMappings, updatedAssets });
        }

        /// <summary>
        /// "212 Outlet" -> 212 manuel atama. Bu mapping'e sahip tum asset'ler otomatik guncellenir.
        /// </summary>
        [HttpPut("store-mappings/{id:int}")]
        public async Task<IActionResult> UpdateStoreMapping(int id, [FromBody] StoreMappingUpdateDto body, CancellationToken ct)
        {
            var m = await _db.StoreNameMappings.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (m is null) return NotFound();

            m.StoreCode = body.StoreCode;
            m.AutoMatched = false;
            m.UpdatedAt = DateTime.UtcNow;

            // Bu raw name'e sahip tum asset'leri de guncelle
            var affected = await _db.InventoryAssets
                .Where(a => a.StoreNameRaw == m.RawName)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.StoreCode, body.StoreCode), ct);

            await _db.SaveChangesAsync(ct);
            await _activity.LogAsync("Inventory", "UpdateStoreMapping", m.RawName,
                $"-> StoreCode {body.StoreCode?.ToString() ?? "null"} ({affected} asset guncellendi)", ct: ct);
            return Ok(new { mapping = m, affectedAssets = affected });
        }

        /// <summary>
        /// Hizli ozet — toplam, magaza ile eslesen, eslesmeyen sayilari.
        /// </summary>
        [HttpGet("summary")]
        public async Task<IActionResult> GetSummary(CancellationToken ct)
        {
            var total = await _db.InventoryAssets.CountAsync(ct);
            var matched = await _db.InventoryAssets.CountAsync(a => a.StoreCode != null, ct);
            var unmatched = total - matched;
            var unmatchedMappings = await _db.StoreNameMappings
                .CountAsync(m => m.StoreCode == null, ct);

            return Ok(new
            {
                totalAssets = total,
                matchedAssets = matched,
                unmatchedAssets = unmatched,
                unmappedStoreNames = unmatchedMappings,
            });
        }
    }
}
