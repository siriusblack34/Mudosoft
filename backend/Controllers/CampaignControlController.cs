using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Data;
using Orchestra.Backend.Services;
using System.Data;
using System.Text.RegularExpressions;

namespace Orchestra.Backend.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/campaign-control")]
    public class CampaignControlController : ControllerBase
    {
        private readonly IRemoteSqlService _sql;
        private readonly OrchestraDbContext _db;
        private readonly ILogger<CampaignControlController> _logger;

        public CampaignControlController(IRemoteSqlService sql, OrchestraDbContext db, ILogger<CampaignControlController> logger)
        {
            _sql = sql;
            _db = db;
            _logger = logger;
        }

        private static (int orchestraCode, int geniusCode) ResolveStoreCodes(int input)
        {
            if (input >= 1000) return (input - 1000, input);
            return (input, input + 1000);
        }

        private static int ToInt(object? o)
        {
            try { return o == null || o == DBNull.Value ? 0 : Convert.ToInt32(o); } catch { return 0; }
        }

        private static decimal ToDec(object? o)
        {
            try { return o == null || o == DBNull.Value ? 0m : Convert.ToDecimal(o); } catch { return 0m; }
        }

        private static string Num(decimal d)
        {
            // Gereksiz ondalıkları at: 500.0000 → "500", 22.2200 → "22,22"
            var v = decimal.Round(d, 2);
            return v == decimal.Truncate(v)
                ? ((long)v).ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("tr-TR"))
                : v.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("tr-TR"));
        }

        // Kampanya etkisini insan-okur metne çevir
        private static string BuildEffect(bool isPoint, string unit, List<(decimal minAmount, decimal discount)> tiers)
        {
            if (tiers.Count == 0) return isPoint ? "Puan kampanyası" : "İndirim";

            if (isPoint)
                return $"%{Num(tiers[0].discount)} puan";

            var withMin = tiers.Where(t => t.minAmount > 0).OrderBy(t => t.minAmount).ToList();
            if (withMin.Count == 0)
            {
                var d = tiers[0].discount;
                if (unit == "%")
                    return d >= 100 ? "Bedelsiz / hediye (%100)" : $"%{Num(d)} indirim";
                return $"{Num(d)} TL indirim";
            }

            // Kademeli tutar eşiği
            return string.Join("  ·  ",
                withMin.Select(t => unit == "%"
                    ? $"≥{Num(t.minAmount)} TL → %{Num(t.discount)}"
                    : $"≥{Num(t.minAmount)} TL → {Num(t.discount)} TL"));
        }

        // Genius müşteri tipi kodları → resmi açıklamalar (CAMPAIGN_CUSTOMER.REC_VALUE)
        private static readonly Dictionary<string, string> CustomerCodeNames = new()
        {
            ["1"]  = "Müşteri",
            ["2"]  = "Kurumsal Müşteri",
            ["3"]  = "Personel - Eski",
            ["4"]  = "VIP Müşteri",
            ["5"]  = "Web Müşteri",
            ["6"]  = "School Müşteri",
            ["7"]  = "Shopamani Müşteri",
            ["8"]  = "BJK Kart",
            ["9"]  = "FB Kart",
            ["10"] = "Müşteri Doğum Günü",
            ["11"] = "Müşteri - Yeni",
            ["12"] = "Web Müşteri - Yeni",
            ["13"] = "Plus Müşteri - Yeni",
            ["14"] = "Personel - Merkez",
            ["15"] = "Personel - Mağaza",
            ["16"] = "School - Yeni",
            ["17"] = "Turist Kart",
            ["18"] = "KoçAilem",
            ["19"] = "YK Müşteri",
            ["20"] = "E-Fatura Müşteri",
            ["21"] = "Mimar Kart",
            ["22"] = "DMS Müşterisi",
            ["23"] = "Personel - Limit Dolu",
            ["24"] = "Personel - Ayrılmış",
            ["25"] = "Personel - Doğum Günü",
            ["26"] = "KoçAilem - Ayrılmış",
            ["27"] = "Personel - Sezonluk İndirim",
            ["28"] = "Bülten Müşterisi",
            ["29"] = "EarlyBird - Concept",
            ["30"] = "EarlyBird - Giyim",
            ["31"] = "Mutfak Churn Müşterisi",
            ["32"] = "Dekoratif Churn Müşterisi",
            ["33"] = "Aydınlatma Churn Müşterisi",
            ["34"] = "Ev Tekstili Churn Müşterisi",
            ["35"] = "Ev Kozmetiği Churn Müşterisi",
            ["36"] = "Kişisel Aksesuar Churn Müşterisi",
            ["37"] = "MHC Churn Müşterisi",
            ["98"] = "Yeni Doğum Günü Puan Test Data",
            ["99"] = "Geçici Personel Doğum Günü",
        };

        private static string CustomerName(string code) =>
            CustomerCodeNames.TryGetValue(code, out var n) ? n : $"Kod {code}";

        private static string CustomerLabel(List<string> codes)
        {
            if (codes.Count == 0) return "Müşteri tipine özel";
            // Çok geniş kitle (kartlı müşteri puan kampanyaları) → kısalt
            if (codes.Count >= 10) return $"Kartlı müşteri ({codes.Count} tip)";
            var named = codes.Select(CustomerName).Distinct().ToList();
            return string.Join(" / ", named);
        }

        // Bağlantı dizisini olan kasa (yoksa PC) cihazını seçer
        private async Task<Orchestra.Backend.Models.StoreDevice?> ResolveDeviceAsync(int orchestraCode, CancellationToken ct)
        {
            return await _db.StoreDevices.AsNoTracking()
                .Where(d => d.StoreCode == orchestraCode &&
                            d.DeviceType.StartsWith("Kasa") &&
                            !string.IsNullOrEmpty(d.DbConnectionString))
                .OrderBy(d => d.DeviceType)
                .FirstOrDefaultAsync(ct)
                ?? await _db.StoreDevices.AsNoTracking()
                    .Where(d => d.StoreCode == orchestraCode &&
                                d.DeviceType == "PC" &&
                                !string.IsNullOrEmpty(d.DbConnectionString))
                    .FirstOrDefaultAsync(ct);
        }

        // ── GET /api/campaign-control/stores ──────────────────────────────
        [HttpGet("stores")]
        public async Task<IActionResult> GetStores(CancellationToken ct)
        {
            var stores = await _db.StoreDevices
                .AsNoTracking()
                .Where(d => d.DeviceType.StartsWith("Kasa") && !string.IsNullOrEmpty(d.DbConnectionString))
                .Select(d => new { d.StoreCode, d.StoreName })
                .Distinct()
                .OrderBy(d => d.StoreCode)
                .ToListAsync(ct);

            return Ok(stores.DistinctBy(s => s.StoreCode)
                            .Select(s => new { storeCode = s.StoreCode, storeName = s.StoreName ?? $"Mağaza {s.StoreCode}" })
                            .ToList());
        }

        // ── GET /api/campaign-control/check ───────────────────────────────
        // 1) STOCK_BARCODE → FK_STOCK_CARD
        // 2) STOCK_CARD_PARAMETER → PARAM_1 listesi (ürün hiyerarşi + promosyon grup değerleri)
        // 3) CAMPAIGN_PRODUCT_RESULT.REC_VALUE ile çarpıştır (COMPARE_TYPE 0=dahil, 3=hariç)
        [HttpGet("check")]
        public async Task<IActionResult> CheckProductCampaigns(
            [FromQuery] string barcode,
            [FromQuery] int storeCode,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(barcode))
                return BadRequest(new { error = "Barkod boş olamaz" });

            barcode = barcode.Trim();
            if (!Regex.IsMatch(barcode, @"^\d{6,20}$"))
                return BadRequest(new { error = "Geçersiz barkod formatı (6-20 haneli rakam bekleniyor)" });

            if (storeCode <= 0)
                return BadRequest(new { error = "Geçersiz mağaza kodu" });

            var (orchestraCode, geniusCode) = ResolveStoreCodes(storeCode);

            var device = await ResolveDeviceAsync(orchestraCode, ct);
            if (device == null || string.IsNullOrEmpty(device.DbConnectionString))
                return NotFound(new { error = $"Mağaza {orchestraCode} için bağlantı bilgisi bulunamadı" });

            var connStr = device.DbConnectionString;

            // ── 1. Barkoddan FK_STOCK_CARD ──────────────────────────────────
            DataTable? barcodeDt;
            try
            {
                barcodeDt = await _sql.ExecuteQueryAsync(connStr,
                    $"SELECT TOP 1 FK_STOCK_CARD FROM STOCK_BARCODE WHERE BARCODE = '{barcode}'");
            }
            catch (Exception ex)
            {
                return StatusCode(503, new { error = $"Barkod sorgusu başarısız: {ex.Message}" });
            }

            if (barcodeDt == null || barcodeDt.Rows.Count == 0)
                return NotFound(new { error = $"'{barcode}' barkoduna ait ürün bulunamadı" });

            var stockCardId = barcodeDt.Rows[0]["FK_STOCK_CARD"]?.ToString()?.Trim() ?? "";
            if (string.IsNullOrEmpty(stockCardId))
                return NotFound(new { error = "FK_STOCK_CARD değeri boş" });

            // ── 2. Ürün adı/kodu (görüntüleme için) ─────────────────────────
            string stokKod = "", stokAdi = "";
            try
            {
                var cardDt = await _sql.ExecuteQueryAsync(connStr,
                    $"SELECT TOP 1 CODE, DESCRIPTION FROM STOCK_CARD WHERE ID = {stockCardId}");
                if (cardDt != null && cardDt.Rows.Count > 0)
                {
                    stokKod = cardDt.Rows[0]["CODE"]?.ToString()?.Trim() ?? "";
                    stokAdi = cardDt.Rows[0]["DESCRIPTION"]?.ToString()?.Trim() ?? "";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("STOCK_CARD okunamadı (ID={ID}): {E}", stockCardId, ex.Message);
            }

            // ── 2b. Birim fiyat (STOCK_PRICE, satış fiyat seviyesi NUM=1) ───
            decimal unitPrice = 0m, labelPrice = 0m;
            try
            {
                var priceDt = await _sql.ExecuteQueryAsync(connStr,
                    $"SELECT TOP 1 UNIT_PRICE, LABEL_PRICE FROM STOCK_PRICE WHERE FK_STOCK_CARD = {stockCardId} AND FK_STORE = {geniusCode} ORDER BY NUM");
                if (priceDt != null && priceDt.Rows.Count > 0)
                {
                    unitPrice  = ToDec(priceDt.Rows[0]["UNIT_PRICE"]);
                    labelPrice = ToDec(priceDt.Rows[0]["LABEL_PRICE"]);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("STOCK_PRICE okunamadı (ID={ID}): {E}", stockCardId, ex.Message);
            }

            // ── 3. STOCK_CARD_PARAMETER → PARAM_1 listesi ───────────────────
            // NUM 1-5 = ürün hiyerarşisi, NUM=9 = promosyon grupları. Hepsini alıyoruz.
            DataTable? paramDt;
            try
            {
                paramDt = await _sql.ExecuteQueryAsync(connStr,
                    $"SELECT NUM, PARAM_1 FROM STOCK_CARD_PARAMETER WHERE FK_STOCK_CARD = {stockCardId} AND PARAM_1 IS NOT NULL");
            }
            catch (Exception ex)
            {
                return StatusCode(503, new { error = $"Ürün parametre sorgusu başarısız: {ex.Message}" });
            }

            var parameters = new List<(string num, string param1)>();
            if (paramDt != null)
            {
                foreach (DataRow row in paramDt.Rows)
                {
                    var p1 = row["PARAM_1"]?.ToString()?.Trim();
                    if (string.IsNullOrEmpty(p1)) continue;
                    var num = paramDt.Columns.Contains("NUM") ? row["NUM"]?.ToString()?.Trim() ?? "" : "";
                    parameters.Add((num, p1));
                }
            }

            var paramValues = parameters.Select(p => p.param1).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            _logger.LogInformation("Kampanya kontrol: barcode={B} cardId={C} param={P}",
                barcode, stockCardId, string.Join(", ", paramValues));

            if (paramValues.Count == 0)
            {
                return Ok(new
                {
                    barcode,
                    storeCode = orchestraCode,
                    geniusStoreCode = geniusCode,
                    deviceId = device.DeviceId,
                    deviceIp = device.CalculatedIpAddress,
                    product = new
                    {
                        stockCardId,
                        code = stokKod,
                        name = stokAdi,
                        unitPrice,
                        labelPrice,
                        parameters = parameters.Select(p => new { num = p.num, param1 = p.param1 }).ToList(),
                        parameterValues = paramValues
                    },
                    campaigns = Array.Empty<object>(),
                    checkedAt = DateTime.UtcNow
                });
            }

            // ── 4a. O an aktif kampanyaları al (sp_GetActiveCampaign {storeId}) ──
            // storeId = mağaza kodu + 1000 (geniusCode). Hem ID hem CODE kümesi topluyoruz.
            var activeIds   = new HashSet<int>();
            var activeCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // SP'den havuz/öncelik (ID bazında): havuz çakışması çözümü için
            var priorityById = new Dictionary<int, int>();
            var poolById      = new Dictionary<int, int>();
            try
            {
                var activeDt = await _sql.ExecuteQueryAsync(connStr, $"EXEC sp_GetActiveCampaign {geniusCode}");
                if (activeDt != null)
                {
                    var idCols   = activeDt.Columns.Cast<DataColumn>()
                        .Where(c => c.ColumnName.Equals("ID", StringComparison.OrdinalIgnoreCase)
                                 || c.ColumnName.Equals("CAMPAIGN_ID", StringComparison.OrdinalIgnoreCase)
                                 || c.ColumnName.Equals("FK_CAMPAIGN", StringComparison.OrdinalIgnoreCase))
                        .Select(c => c.ColumnName).ToList();
                    var codeCols = activeDt.Columns.Cast<DataColumn>()
                        .Where(c => c.ColumnName.Equals("CODE", StringComparison.OrdinalIgnoreCase)
                                 || c.ColumnName.Equals("CAMPAIGN_CODE", StringComparison.OrdinalIgnoreCase))
                        .Select(c => c.ColumnName).ToList();
                    bool hasPriority = activeDt.Columns.Contains("PRIORITY");
                    bool hasPool     = activeDt.Columns.Contains("POOL_NUM");
                    var firstIdCol = idCols.FirstOrDefault();

                    foreach (DataRow r in activeDt.Rows)
                    {
                        foreach (var ic in idCols)
                            if (int.TryParse(r[ic]?.ToString()?.Trim(), out var aid)) activeIds.Add(aid);
                        foreach (var cc in codeCols)
                        {
                            var code = r[cc]?.ToString()?.Trim();
                            if (!string.IsNullOrEmpty(code)) activeCodes.Add(code);
                        }
                        if (firstIdCol != null && int.TryParse(r[firstIdCol]?.ToString()?.Trim(), out var rid))
                        {
                            if (hasPriority) priorityById[rid] = ToInt(r["PRIORITY"]);
                            if (hasPool)     poolById[rid]     = ToInt(r["POOL_NUM"]);
                        }
                    }
                }
                _logger.LogInformation("sp_GetActiveCampaign {Store}: {Id} ID, {Code} CODE aktif kampanya",
                    geniusCode, activeIds.Count, activeCodes.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("sp_GetActiveCampaign başarısız (store={S}): {E}", geniusCode, ex.Message);
                return StatusCode(503, new { error = $"Aktif kampanya sorgusu başarısız: {ex.Message}" });
            }

            // ── 4b. CAMPAIGN_PRODUCT_RESULT ile çarpıştır ───────────────────
            var inList = string.Join(",", paramValues.Select(p => $"'{p.Replace("'", "''")}'"));
            var campaignSql = $@"
SELECT
    cpr.FK_CAMPAIGN,
    cpr.REC_VALUE,
    cpr.COMPARE_TYPE,
    c.CODE        AS CAMPAIGN_CODE,
    c.DESCRIPTION AS CAMPAIGN_DESC
FROM CAMPAIGN_PRODUCT_RESULT cpr
INNER JOIN CAMPAIGN c ON c.ID = cpr.FK_CAMPAIGN
WHERE cpr.REC_VALUE IN ({inList})
ORDER BY cpr.FK_CAMPAIGN, cpr.COMPARE_TYPE";

            DataTable? campaignDt;
            try
            {
                campaignDt = await _sql.ExecuteQueryAsync(connStr, campaignSql);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Kampanya sorgusu başarısız (store={S}): {E}", orchestraCode, ex.Message);
                return StatusCode(503, new { error = $"Kampanya sorgu hatası: {ex.Message}" });
            }

            // ── 5. Dahil / Hariç mantığı ────────────────────────────────────
            // Dahil = en az bir COMPARE_TYPE=0 eşleşme VAR ve hiç COMPARE_TYPE=3 eşleşme YOK
            var rows = new List<(int campaignId, string code, string desc, string recValue, int compareType)>();
            if (campaignDt != null)
            {
                foreach (DataRow row in campaignDt.Rows)
                {
                    string CStr(string col) => campaignDt.Columns.Contains(col) && row[col] != DBNull.Value ? row[col]?.ToString()?.Trim() ?? "" : "";
                    int CInt(string col) { try { return campaignDt.Columns.Contains(col) && row[col] != DBNull.Value ? Convert.ToInt32(row[col]) : 0; } catch { return 0; } }
                    rows.Add((CInt("FK_CAMPAIGN"), CStr("CAMPAIGN_CODE"), CStr("CAMPAIGN_DESC"),
                              CStr("REC_VALUE"), CInt("COMPARE_TYPE")));
                }
            }

            // Aktif kampanya filtresi: SP boş dönmediyse uygula (ID veya CODE eşleşmesi)
            bool hasActiveList = activeIds.Count > 0 || activeCodes.Count > 0;
            bool IsActive(int id, string code) =>
                !hasActiveList || activeIds.Contains(id) || (!string.IsNullOrEmpty(code) && activeCodes.Contains(code));

            // Eşleşen + aktif kampanyaları grupla
            var grouped = rows
                .GroupBy(r => r.campaignId)
                .Where(g => IsActive(g.Key, g.First().code))
                .ToList();

            var campIds = grouped.Select(g => g.Key).Distinct().ToList();

            // ── 6. Koşul zenginleştirme (tip, etki, müşteri kapsamı, eşik) ───
            // DTYPE (CAMPAIGN_DISCOUNT_DEF, join via NUM): 1=otomatik indirim, 2=puan, 3=çek/kod gerekli
            var headerById = new Dictionary<int, (int campaignType, int amountType, int giftType, int dtype)>();
            var tiersById  = new Dictionary<int, List<(decimal minAmount, decimal discount)>>();
            var custById   = new Dictionary<int, List<(string recValue, int compareType)>>();

            if (campIds.Count > 0)
            {
                var idCsv = string.Join(",", campIds);
                try
                {
                    var hdt = await _sql.ExecuteQueryAsync(connStr,
                        $@"SELECT c.ID, c.CAMPAIGN_TYPE, c.AMOUNT_TYPE, c.GIFT_TYPE, def.DTYPE
                           FROM CAMPAIGN c
                           LEFT JOIN CAMPAIGN_DISCOUNT_DEF def ON def.NUM = c.CK_CAMPAIGN_DISCOUNT_DEF
                           WHERE c.ID IN ({idCsv})");
                    if (hdt != null)
                        foreach (DataRow r in hdt.Rows)
                        {
                            int id = ToInt(r["ID"]);
                            headerById[id] = (ToInt(r["CAMPAIGN_TYPE"]), ToInt(r["AMOUNT_TYPE"]), ToInt(r["GIFT_TYPE"]), ToInt(r["DTYPE"]));
                        }
                }
                catch (Exception ex) { _logger.LogWarning("CAMPAIGN header okunamadı: {E}", ex.Message); }

                try
                {
                    var ddt = await _sql.ExecuteQueryAsync(connStr,
                        $"SELECT FK_CAMPAIGN, MIN_AMOUNT, DISCOUNT FROM CAMPAIGN_DISCOUNT WHERE FK_CAMPAIGN IN ({idCsv}) ORDER BY FK_CAMPAIGN, MIN_AMOUNT");
                    if (ddt != null)
                        foreach (DataRow r in ddt.Rows)
                        {
                            int id = ToInt(r["FK_CAMPAIGN"]);
                            if (!tiersById.TryGetValue(id, out var list)) { list = new(); tiersById[id] = list; }
                            list.Add((ToDec(r["MIN_AMOUNT"]), ToDec(r["DISCOUNT"])));
                        }
                }
                catch (Exception ex) { _logger.LogWarning("CAMPAIGN_DISCOUNT okunamadı: {E}", ex.Message); }

                try
                {
                    var cdt = await _sql.ExecuteQueryAsync(connStr,
                        $"SELECT FK_CAMPAIGN, REC_VALUE, COMPARE_TYPE FROM CAMPAIGN_CUSTOMER WHERE FK_CAMPAIGN IN ({idCsv})");
                    if (cdt != null)
                        foreach (DataRow r in cdt.Rows)
                        {
                            int id = ToInt(r["FK_CAMPAIGN"]);
                            if (!custById.TryGetValue(id, out var list)) { list = new(); custById[id] = list; }
                            list.Add((r["REC_VALUE"]?.ToString()?.Trim() ?? "", ToInt(r["COMPARE_TYPE"])));
                        }
                }
                catch (Exception ex) { _logger.LogWarning("CAMPAIGN_CUSTOMER okunamadı: {E}", ex.Message); }
            }

            var campaigns = grouped
                .Select(g =>
                {
                    int id = g.Key;
                    var incl = g.Where(r => r.compareType == 0)
                                .Select(r => new { recValue = r.recValue, compareType = r.compareType }).ToList();
                    var excl = g.Where(r => r.compareType == 3)
                                .Select(r => new { recValue = r.recValue, compareType = r.compareType }).ToList();

                    headerById.TryGetValue(id, out var hdr);
                    var tiers = tiersById.TryGetValue(id, out var t) ? t : new List<(decimal minAmount, decimal discount)>();
                    var custs = custById.TryGetValue(id, out var cu) ? cu : new List<(string recValue, int compareType)>();

                    // DTYPE öncelikli: 2=puan, 3=çek/kod, 1/diğer=otomatik indirim
                    // (DTYPE 0/eksikse CAMPAIGN_TYPE'a düş)
                    string kind = hdr.dtype switch
                    {
                        2 => "puan",
                        3 => "cek",
                        1 => "indirim",
                        _ => hdr.campaignType == 1 ? "puan" : "indirim"
                    };
                    bool isPoint = kind == "puan";
                    string unit  = (isPoint || hdr.amountType == 9) ? "%" : "TL";

                    // Müşteri kapsamı: *** veya * = herkes (müşterisiz dahil)
                    var inclCust = custs.Where(c => c.compareType == 0).Select(c => c.recValue).ToList();
                    bool allCustomers = inclCust.Count == 0 || inclCust.Any(v => v == "***" || v == "*");
                    var custCodes = inclCust.Where(v => v != "***" && v != "*").Distinct().ToList();

                    // En düşük tutar eşiği (>0)
                    decimal minThreshold = tiers.Where(x => x.minAmount > 0).Select(x => x.minAmount)
                                                .DefaultIfEmpty(0).Min();

                    return new
                    {
                        campaignId    = id,
                        campaignCode  = g.First().code,
                        campaignDesc  = g.First().desc,
                        isIncluded    = incl.Count > 0 && excl.Count == 0,
                        includeRules  = incl,
                        excludeRules  = excl,
                        matchedParams = g.Select(r => r.recValue).Distinct().ToList(),
                        // zenginleştirme
                        kind,
                        unit,
                        dtype         = hdr.dtype,
                        requiresCode  = kind == "cek",
                        poolNum       = poolById.TryGetValue(id, out var pn) ? pn : 0,
                        priority      = priorityById.TryGetValue(id, out var pr) ? pr : 0,
                        effect        = BuildEffect(isPoint, unit, tiers),
                        tiers         = tiers.Select(x => new { minAmount = x.minAmount, discount = x.discount }).ToList(),
                        customerScope = allCustomers ? "all" : "typed",
                        customerLabel = allCustomers ? "Herkes" : CustomerLabel(custCodes),
                        customerCodes = custCodes,
                        customerNames = custCodes.Select(c => new { code = c, name = CustomerName(c) }).ToList(),
                        minThreshold
                    };
                })
                .OrderByDescending(c => c.isIncluded)
                .ThenBy(c => c.campaignId)
                .ToList();

            _logger.LogInformation("Kampanya kontrol sonucu: barcode={B} store={S} param={P} aktif={A} eşleşen={K} (dahil={D})",
                barcode, orchestraCode, paramValues.Count, activeIds.Count + activeCodes.Count, campaigns.Count, campaigns.Count(c => c.isIncluded));

            return Ok(new
            {
                barcode,
                storeCode       = orchestraCode,
                geniusStoreCode = geniusCode,
                deviceId        = device.DeviceId,
                deviceIp        = device.CalculatedIpAddress,
                product         = new
                {
                    stockCardId,
                    code            = stokKod,
                    name            = stokAdi,
                    unitPrice,
                    labelPrice,
                    parameters      = parameters.Select(p => new { num = p.num, param1 = p.param1 }).ToList(),
                    parameterValues = paramValues
                },
                campaigns,
                checkedAt = DateTime.UtcNow
            });
        }
    }
}
