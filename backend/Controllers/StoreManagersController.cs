using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Data;
using Orchestra.Backend.Models;

namespace Orchestra.Backend.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/[controller]")]
    public class StoreManagersController : ControllerBase
    {
        private readonly OrchestraDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly ILogger<StoreManagersController> _logger;

        public StoreManagersController(
            OrchestraDbContext context,
            IConfiguration configuration,
            ILogger<StoreManagersController> logger)
        {
            _context = context;
            _configuration = configuration;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> GetStoreManagers()
        {
            var managers = await _context.StoreManagers.ToListAsync();

            // StoreDevices tablosundan gerçek mağaza kodlarını al (mağaza adına göre eşleştir)
            var storeCodeMap = await _context.StoreDevices
                .AsNoTracking()
                .Where(d => d.DeviceType == "PC")
                .GroupBy(d => d.StoreName)
                .Select(g => new { StoreName = g.Key, ActualStoreCode = g.First().StoreCode })
                .ToDictionaryAsync(x => x.StoreName, x => x.ActualStoreCode);

            var result = managers.Select(m =>
            {
                storeCodeMap.TryGetValue(m.StoreName, out var actualCode);
                return new
                {
                    m.Id,
                    m.StoreCode,
                    m.StoreName,
                    m.FullName,
                    m.Phone,
                    m.Address,
                    ActualStoreCode = actualCode > 0 ? actualCode : (int?)null
                };
            });

            return Ok(result);
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

        // ── GENIUS3 STORE tablosundan adresleri senkronize et ────────────────
        // STORE.NUM == StoreManager.StoreCode eşleşmesi üzerinden HEADER_4/5/6
        // birleştirilerek Address kolonuna yazılır.
        [HttpPost("sync-addresses")]
        public async Task<IActionResult> SyncAddressesFromGenius()
        {
            var server = _configuration["GeniusDb:CentralServer"]
                ?? Environment.GetEnvironmentVariable("GENIUS_DB_CENTRAL_SERVER")
                ?? "GeniusDBLive.mudo.com.tr";
            var user = _configuration["GeniusDb:Username"]
                ?? Environment.GetEnvironmentVariable("GENIUS_DB_USER")
                ?? "GENIUS3";
            var pass = _configuration["GeniusDb:Password"]
                ?? Environment.GetEnvironmentVariable("GENIUS_DB_PASSWORD");

            if (string.IsNullOrWhiteSpace(pass))
                return StatusCode(500, new { error = "GENIUS_DB_PASSWORD tanımlı değil." });

            var connStr = $"Server={server};Database=Genius3;User Id={user};Password={pass};TrustServerCertificate=True;Encrypt=False;Connect Timeout=15;";

            // STORE'dan NUM + DESCRIPTION + adres üçlüsünü çek
            var byNum = new Dictionary<int, string>();
            var byName = new Dictionary<string, string>();
            try
            {
                await using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();
                await using var cmd = new SqlCommand(
                    "SELECT NUM, DESCRIPTION, HEADER_4, HEADER_5, HEADER_6 FROM STORE", conn);
                cmd.CommandTimeout = 30;
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var num = reader.IsDBNull(0) ? (int?)null : Convert.ToInt32(reader.GetValue(0));
                    var desc = reader.IsDBNull(1) ? "" : reader.GetString(1).Trim();
                    var h4 = reader.IsDBNull(2) ? "" : reader.GetString(2).Trim();
                    var h5 = reader.IsDBNull(3) ? "" : reader.GetString(3).Trim();
                    var h6 = reader.IsDBNull(4) ? "" : reader.GetString(4).Trim();
                    var combined = string.Join(", ", new[] { h4, h5, h6 }.Where(s => !string.IsNullOrWhiteSpace(s)));
                    if (string.IsNullOrWhiteSpace(combined)) continue;
                    if (num.HasValue) byNum[num.Value] = combined;
                    var norm = NormalizeStoreName(desc);
                    if (!string.IsNullOrEmpty(norm)) byName[norm] = combined;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GENIUS3 STORE sorgusu başarısız ({Server})", server);
                return StatusCode(502, new { error = $"GENIUS3 sorgusu başarısız: {ex.Message}" });
            }

            if (byNum.Count == 0 && byName.Count == 0)
                return Ok(new { fetched = 0, updated = 0, missingCount = 0, missingCodes = new List<int>(), message = "STORE tablosu boş döndü." });

            var managers = await _context.StoreManagers.ToListAsync();
            var updated = 0;
            var matchedByNum = 0;
            var matchedByName = 0;
            var missing = new List<int>();

            foreach (var m in managers)
            {
                string? addr = null;
                if (byNum.TryGetValue(m.StoreCode, out var byNumAddr))
                {
                    addr = byNumAddr;
                    matchedByNum++;
                }
                else
                {
                    var norm = NormalizeStoreName(m.StoreName);
                    if (!string.IsNullOrEmpty(norm) && byName.TryGetValue(norm, out var byNameAddr))
                    {
                        addr = byNameAddr;
                        matchedByName++;
                    }
                }

                if (addr != null)
                {
                    if (m.Address != addr)
                    {
                        m.Address = addr;
                        updated++;
                    }
                }
                else
                {
                    missing.Add(m.StoreCode);
                }
            }
            await _context.SaveChangesAsync();

            return Ok(new
            {
                fetched = Math.Max(byNum.Count, byName.Count),
                updated,
                matchedByNum,
                matchedByName,
                missingCount = missing.Count,
                missingCodes = missing.OrderBy(c => c).Take(20).ToList(),
            });
        }

        // İsim eşleştirmesi için normalleştirme: küçük harf, "giyim/mudo/outlet/concept/marina/city"
        // gibi yaygın eklerden temizle, boşlukları ve diakritikleri kaldır.
        private static string NormalizeStoreName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            var s = raw.ToLowerInvariant()
                .Replace("ı", "i").Replace("ş", "s").Replace("ğ", "g")
                .Replace("ü", "u").Replace("ö", "o").Replace("ç", "c")
                .Replace("İ", "i").Replace("Ş", "s").Replace("Ğ", "g")
                .Replace("Ü", "u").Replace("Ö", "o").Replace("Ç", "c");

            // Yaygın takıları sil
            string[] noise = { " giyim", " mudo", " outlet", " concept", " marina", " city",
                               " forum", " avm", " a.v.m", " a v m", " park",
                               " mağazası", " magazasi", " magaza", " store" };
            foreach (var n in noise)
                s = s.Replace(n, " ");

            // Sadece alfa-numerik karakterler kalsın
            var sb = new StringBuilder();
            foreach (var c in s)
                if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.ToString();
        }
    }

    public class ImportRequest
    {
        public string RawText { get; set; } = string.Empty;
    }
}
