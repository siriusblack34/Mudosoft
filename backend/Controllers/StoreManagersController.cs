using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MudoSoft.Backend.Data;
using MudoSoft.Backend.Models;

namespace MudoSoft.Backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StoreManagersController : ControllerBase
    {
        private readonly MudoSoftDbContext _context;

        public StoreManagersController(MudoSoftDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetStoreManagers()
        {
            var managers = await _context.StoreManagers.ToListAsync();
            return Ok(managers);
        }

        [HttpPost("import")]
        public async Task<IActionResult> ImportStoreManagers([FromBody] ImportRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.RawText))
            {
                return BadRequest("No data provided.");
            }

            var lines = request.RawText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var managersToUpsert = new List<StoreManager>();

            foreach (var line in lines)
            {
                // When copied from Excel, there might be multiple tabs or empty trailing elements.
                var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).ToArray();
                
                // Format could be 4 columns: Kod | Mağaza Adı | Ad Soyad | Telefon
                // Or 6 columns: Kod | Mağaza Adı | Ad Soyad | Pozisyon | İl | Telefon
                if (parts.Length >= 4)
                {
                    if (int.TryParse(parts[0], out int code))
                    {
                        var storeName = parts[1];
                        var fullName = parts[2];
                        
                        // The phone number is typically the last element
                        var phoneRaw = parts[parts.Length - 1];
                        var telPhone = phoneRaw.Length > 20 ? phoneRaw.Substring(0, 20) : phoneRaw;

                        managersToUpsert.Add(new StoreManager
                        {
                            StoreCode = code,
                            StoreName = storeName,
                            FullName = fullName,
                            Phone = telPhone
                        });
                    }
                }
            }

            if (!managersToUpsert.Any())
            {
                return BadRequest("No valid rows could be parsed.");
            }

            // Deduplicate by StoreCode just in case the Excel has double entries.
            var uniqueManagers = managersToUpsert
                .GroupBy(m => m.StoreCode)
                .Select(g => g.Last())
                .ToList();

            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Simple wipe and replace or upsert. Let's do clear & insert since it's an import of the whole table.
                _context.StoreManagers.RemoveRange(_context.StoreManagers);
                await _context.SaveChangesAsync();

                await _context.StoreManagers.AddRangeAsync(uniqueManagers);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();

                return Ok(new { success = true, count = uniqueManagers.Count });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, $"An error occurred: {ex.Message}");
            }
        }
    }

    public class ImportRequest
    {
        public string RawText { get; set; } = string.Empty;
    }
}
