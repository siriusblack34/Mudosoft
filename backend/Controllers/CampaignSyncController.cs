using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Data;
using Orchestra.Backend.Services;
using System.Collections.Concurrent;
using System.Data;

namespace Orchestra.Backend.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/campaign-sync")]
    public class CampaignSyncController : ControllerBase
    {
        private readonly IRemoteSqlService _sql;
        private readonly OrchestraDbContext _db;
        private readonly ILogger<CampaignSyncController> _logger;

        public CampaignSyncController(IRemoteSqlService sql, OrchestraDbContext db, ILogger<CampaignSyncController> logger)
        {
            _sql = sql;
            _db = db;
            _logger = logger;
        }

        private string BuildMerkezConnectionString()
        {
            var user = Environment.GetEnvironmentVariable("GENIUS_DB_USER") ?? "sa";
            var password = Environment.GetEnvironmentVariable("GENIUS_DB_PASSWORD") ?? "";
            return $"Server=GeniusDBLive.mudo.com.tr;Database=Genius3;User Id={user};Password={password};TrustServerCertificate=True;Connect Timeout=15;";
        }

        private static string WithTightTimeout(string connStr, int seconds = 6)
        {
            try { return new SqlConnectionStringBuilder(connStr) { ConnectTimeout = seconds }.ConnectionString; }
            catch { return connStr; }
        }

        // GeniusPos FK_STORE değeri = Orchestra StoreCode + 1000
        // Örnek: Orchestra 107  →  GeniusPos 1107
        // Endpoint hem 107 (Orchestra kodu) hem 1107 (GeniusPos ID) kabul eder.
        private static (int orchestraCode, int geniusCode) ResolveStoreCodes(int input)
        {
            if (input >= 1000)
                return (input - 1000, input);        // kullanıcı GeniusPos ID girdi
            return (input, input + 1000);            // kullanıcı Orchestra kodu girdi
        }

        // ── GET /api/campaign-sync/{storeCode}/check  ─────────────────────
        [HttpGet("{storeCode}/check")]
        public async Task<IActionResult> CheckCampaignSync(int storeCode, CancellationToken ct)
        {
            if (storeCode <= 0)
                return BadRequest(new { error = "Geçersiz mağaza kodu" });

            var (orchestraCode, geniusCode) = ResolveStoreCodes(storeCode);
            var merkezConn = BuildMerkezConnectionString();

            var merkezCampaigns = new List<CampaignRow>();
            string? merkezError = null;
            try
            {
                var dt = await _sql.ExecuteQueryAsync(merkezConn, $"EXEC sp_GetActiveCampaign {geniusCode}");
                if (dt != null) merkezCampaigns = ParseCampaigns(dt);
            }
            catch (Exception ex)
            {
                merkezError = ex.Message;
                _logger.LogWarning("Merkez kampanya sorgusu başarısız (genius={GeniusCode}): {Err}", geniusCode, ex.Message);
            }

            var merkezById = merkezCampaigns.ToDictionary(c => c.Id);

            var devices = await _db.StoreDevices
                .AsNoTracking()
                .Where(d => d.StoreCode == orchestraCode && (d.DeviceType == "PC" || d.DeviceType.StartsWith("Kasa")))
                .OrderBy(d => d.DeviceType)
                .ToListAsync(ct);

            var deviceResults = await Task.WhenAll(devices.Select(d =>
                CheckDeviceAsync(d, geniusCode, merkezById)));

            _logger.LogInformation("Kampanya senkron: orchestra={OCode} genius={GCode} merkez={MC} cihaz={DC}",
                orchestraCode, geniusCode, merkezCampaigns.Count, devices.Count);

            return Ok(new
            {
                storeCode = orchestraCode,
                geniusStoreCode = geniusCode,
                checkedAt = DateTime.UtcNow,
                merkezError,
                merkezCount = merkezCampaigns.Count,
                devices = deviceResults
            });
        }

        // ── GET /api/campaign-sync/all/check  ─────────────────────────────
        [HttpGet("all/check")]
        public async Task<IActionResult> CheckAllStores(CancellationToken ct)
        {
            var merkezConn = BuildMerkezConnectionString();

            var allDevices = await _db.StoreDevices
                .AsNoTracking()
                .Where(d => d.DeviceType == "PC" || d.DeviceType.StartsWith("Kasa"))
                .OrderBy(d => d.StoreCode).ThenBy(d => d.DeviceType)
                .ToListAsync(ct);

            var storeGroups = allDevices
                .GroupBy(d => d.StoreCode)
                .Select(g => new { OrchestraCode = g.Key, GeniusCode = g.Key + 1000, StoreName = g.First().StoreName, Devices = g.ToList() })
                .ToList();

            var sem = new SemaphoreSlim(10);
            var resultDict = new ConcurrentDictionary<int, object>();

            var tasks = storeGroups.Select(async sg =>
            {
                await sem.WaitAsync(ct);
                try
                {
                    var merkezCampaigns = new List<CampaignRow>();
                    string? merkezError = null;
                    try
                    {
                        var dt = await _sql.ExecuteQueryAsync(merkezConn, $"EXEC sp_GetActiveCampaign {sg.GeniusCode}");
                        if (dt != null) merkezCampaigns = ParseCampaigns(dt);
                    }
                    catch (Exception ex) { merkezError = ex.Message; }

                    var merkezById = merkezCampaigns.ToDictionary(c => c.Id);

                    var deviceTasks = sg.Devices.Select(d =>
                        CheckDeviceAsync(d, sg.GeniusCode, merkezById, tightTimeout: true));
                    var deviceResults = await Task.WhenAll(deviceTasks);

                    resultDict[sg.OrchestraCode] = new
                    {
                        storeCode    = sg.OrchestraCode,
                        geniusCode   = sg.GeniusCode,
                        storeName    = sg.StoreName,
                        merkezCount  = merkezCampaigns.Count,
                        merkezError,
                        devices      = deviceResults
                    };
                }
                catch (Exception ex)
                {
                    resultDict[sg.OrchestraCode] = new
                    {
                        storeCode    = sg.OrchestraCode,
                        geniusCode   = sg.OrchestraCode + 1000,
                        storeName    = sg.StoreName ?? "",
                        merkezCount  = 0,
                        merkezError  = ex.Message,
                        devices      = Array.Empty<object>()
                    };
                }
                finally { sem.Release(); }
            });

            await Task.WhenAll(tasks);

            var ordered = resultDict.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToList();
            _logger.LogInformation("Tüm mağaza kampanya kontrolü: {Total} mağaza", storeGroups.Count);

            return Ok(new
            {
                checkedAt  = DateTime.UtcNow,
                totalStores = storeGroups.Count,
                stores     = ordered
            });
        }

        // ── Cihaz kontrolü ────────────────────────────────────────────────
        private async Task<object> CheckDeviceAsync(
            Models.StoreDevice device,
            int geniusStoreCode,
            Dictionary<int, CampaignRow> merkezById,
            bool tightTimeout = false)
        {
            if (string.IsNullOrWhiteSpace(device.DbConnectionString))
            {
                return new
                {
                    deviceId = device.DeviceId, deviceName = device.DeviceName,
                    deviceType = device.DeviceType, ipAddress = device.CalculatedIpAddress,
                    status = "no_conn", error = "Connection string tanımlı değil",
                    merkezCount = merkezById.Count, deviceCount = 0,
                    missingCampaigns = Array.Empty<object>()
                };
            }

            var connStr = tightTimeout
                ? WithTightTimeout(device.DbConnectionString)
                : device.DbConnectionString;

            List<CampaignRow> deviceCampaigns;
            try
            {
                var dt = await _sql.ExecuteQueryAsync(connStr, $"EXEC sp_GetActiveCampaign {geniusStoreCode}");
                deviceCampaigns = dt != null ? ParseCampaigns(dt) : new List<CampaignRow>();
            }
            catch (Exception ex)
            {
                return new
                {
                    deviceId = device.DeviceId, deviceName = device.DeviceName,
                    deviceType = device.DeviceType, ipAddress = device.CalculatedIpAddress,
                    status = "offline", error = ex.Message,
                    merkezCount = merkezById.Count, deviceCount = 0,
                    missingCampaigns = Array.Empty<object>()
                };
            }

            var deviceById = deviceCampaigns.ToDictionary(c => c.Id);
            var missingIds = merkezById.Keys.Where(id => !deviceById.ContainsKey(id)).OrderBy(id => id).ToList();

            var missingCampaigns = new List<object>();
            foreach (var campaignId in missingIds)
            {
                var merkez = merkezById[campaignId];
                var tableChecks = await CheckCampaignTablesAsync(connStr, campaignId, merkez, geniusStoreCode);
                missingCampaigns.Add(new
                {
                    id = campaignId, code = merkez.Code, description = merkez.Description,
                    fkCampaignPeriod = merkez.FkCampaignPeriod,
                    ckCampaignDiscountDef = merkez.CkCampaignDiscountDef,
                    tableChecks
                });
            }

            return new
            {
                deviceId = device.DeviceId, deviceName = device.DeviceName,
                deviceType = device.DeviceType, ipAddress = device.CalculatedIpAddress,
                status = missingCampaigns.Count > 0 ? "missing" : "ok",
                error = (string?)null,
                merkezCount = merkezById.Count, deviceCount = deviceCampaigns.Count,
                missingCampaigns
            };
        }

        // ── 7 tablo kontrolü (tek SQL sorgusu) ───────────────────────────
        private async Task<List<object>> CheckCampaignTablesAsync(
            string connStr, int campaignId, CampaignRow merkez, int geniusStoreCode)
        {
            var periodId      = merkez.FkCampaignPeriod;
            var discountDefId = merkez.CkCampaignDiscountDef;

            var periodSql      = periodId > 0      ? $"(SELECT COUNT(*) FROM CAMPAIGN_PERIOD WHERE ID={periodId})"                    : "1";
            var discountDefSql = discountDefId > 0 ? $"(SELECT COUNT(*) FROM CAMPAIGN_DISCOUNT_DEF WHERE ID={discountDefId})"        : "1";

            var sql = $@"SELECT
  (SELECT COUNT(*) FROM CAMPAIGN WHERE ID={campaignId}) AS HasCampaign,
  {periodSql} AS HasPeriod,
  (SELECT COUNT(*) FROM CAMPAIGN_PRODUCT_RESULT WHERE FK_CAMPAIGN={campaignId}) AS HasProductResult,
  (SELECT COUNT(*) FROM CAMPAIGN_PRODUCT_SOURCE WHERE FK_CAMPAIGN={campaignId}) AS HasProductSource,
  (SELECT COUNT(*) FROM CAMPAIGN_DISCOUNT WHERE FK_CAMPAIGN={campaignId}) AS HasDiscount,
  (SELECT COUNT(*) FROM CAMPAIGN_STORE WHERE FK_STORE={geniusStoreCode} AND ACTIVE=1 AND FK_CAMPAIGN={campaignId}) AS HasStore,
  {discountDefSql} AS HasDiscountDef";

            var checks = new List<object>();
            try
            {
                var dt = await _sql.ExecuteQueryAsync(connStr, sql);
                if (dt?.Rows.Count > 0)
                {
                    var row = dt.Rows[0];
                    int V(string col) => row.Table.Columns.Contains(col) && row[col] != DBNull.Value ? Convert.ToInt32(row[col]) : 0;

                    checks.Add(new { table = "CAMPAIGN",                exists = V("HasCampaign") > 0,       count = V("HasCampaign"),       note = $"ID={campaignId}" });
                    checks.Add(new { table = "CAMPAIGN_PERIOD",         exists = V("HasPeriod") > 0,          count = V("HasPeriod"),          note = periodId > 0 ? $"ID={periodId}" : "ID=0 (atlandı)" });
                    checks.Add(new { table = "CAMPAIGN_PRODUCT_RESULT", exists = V("HasProductResult") > 0,   count = V("HasProductResult"),   note = $"FK_CAMPAIGN={campaignId}" });
                    checks.Add(new { table = "CAMPAIGN_PRODUCT_SOURCE", exists = V("HasProductSource") > 0,   count = V("HasProductSource"),   note = $"FK_CAMPAIGN={campaignId}" });
                    checks.Add(new { table = "CAMPAIGN_DISCOUNT",       exists = V("HasDiscount") > 0,         count = V("HasDiscount"),         note = $"FK_CAMPAIGN={campaignId}" });
                    checks.Add(new { table = "CAMPAIGN_STORE",          exists = V("HasStore") > 0,            count = V("HasStore"),            note = $"FK_STORE={geniusStoreCode}, ACTIVE=1" });
                    checks.Add(new { table = "CAMPAIGN_DISCOUNT_DEF",   exists = V("HasDiscountDef") > 0,      count = V("HasDiscountDef"),      note = discountDefId > 0 ? $"ID={discountDefId}" : "ID=0 (atlandı)" });
                }
            }
            catch (Exception ex)
            {
                checks.Add(new { table = "KONTROL_HATASI", exists = false, count = 0, note = ex.Message });
            }
            return checks;
        }

        private static List<CampaignRow> ParseCampaigns(DataTable dt)
        {
            var result = new List<CampaignRow>();
            foreach (DataRow row in dt.Rows)
            {
                int GetInt(string col) { if (!dt.Columns.Contains(col) || row[col] == DBNull.Value) return 0; try { return Convert.ToInt32(row[col]); } catch { return 0; } }
                string GetStr(string col) { if (!dt.Columns.Contains(col) || row[col] == DBNull.Value) return ""; return row[col]?.ToString()?.Trim() ?? ""; }
                var id = GetInt("ID");
                if (id <= 0) continue;
                result.Add(new CampaignRow { Id = id, Code = GetStr("CODE"), Description = GetStr("DESCRIPTION"), FkCampaignPeriod = GetInt("FK_CAMPAIGN_PERIOD"), CkCampaignDiscountDef = GetInt("CK_CAMPAIGN_DISCOUNT_DEF") });
            }
            return result;
        }

        private sealed class CampaignRow
        {
            public int Id { get; set; }
            public string Code { get; set; } = "";
            public string Description { get; set; } = "";
            public int FkCampaignPeriod { get; set; }
            public int CkCampaignDiscountDef { get; set; }
        }
    }
}
