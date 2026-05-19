using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Data;
using Orchestra.Backend.Models;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Orchestra.Backend.Services
{
    public class InventoryImportResult
    {
        public int BatchId { get; set; }
        public int TotalRows { get; set; }
        public int InsertedCount { get; set; }
        public int UpdatedCount { get; set; }
        public int SkippedCount { get; set; }
        public int UnmatchedStoreCount { get; set; }
        public List<string> UnmatchedStoreNames { get; set; } = new();
        public List<string> Errors { get; set; } = new();
    }

    public class InventoryImportService
    {
        private readonly OrchestraDbContext _db;
        private readonly ILogger<InventoryImportService> _log;

        // SDP basliklari -> entity property
        private static readonly Dictionary<string, string> HeaderMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Asset Name"] = nameof(InventoryAsset.AssetName),
            ["User"] = nameof(InventoryAsset.StoreNameRaw),
            ["Department"] = nameof(InventoryAsset.Department),
            ["Product Type"] = nameof(InventoryAsset.ProductType),
            ["Product"] = nameof(InventoryAsset.Product),
            ["ürün Kodu"] = nameof(InventoryAsset.ProductCode),
            ["Urun Kodu"] = nameof(InventoryAsset.ProductCode),
            ["Ürün Kodu"] = nameof(InventoryAsset.ProductCode),
            ["Org Serial Number"] = nameof(InventoryAsset.OrgSerialNumber),
            ["Computer Name"] = nameof(InventoryAsset.ComputerName),
            ["MAC Adresi"] = nameof(InventoryAsset.MacAddress),
            ["Asset Tag"] = nameof(InventoryAsset.AssetTag),
            ["Acquisition Date"] = nameof(InventoryAsset.AcquisitionDate),
            ["Expiry Date"] = nameof(InventoryAsset.ExpiryDate),
            ["Yazarkasa Sicil No"] = nameof(InventoryAsset.YazarkasaSicilNo),
            ["Base Seri No"] = nameof(InventoryAsset.BaseSeriNo),
            ["Printer Seri No"] = nameof(InventoryAsset.PrinterSeriNo),
            ["2. Monitor Seri No"] = nameof(InventoryAsset.IkinciMonitorSeriNo),
            ["IP Adresi"] = nameof(InventoryAsset.IpAddress),
            ["Asset State"] = nameof(InventoryAsset.AssetState),
            ["Fiziksel Durum"] = nameof(InventoryAsset.FizikselDurum),
            ["Purchase Cost"] = nameof(InventoryAsset.PurchaseCost),
            ["Fatura No"] = nameof(InventoryAsset.FaturaNo),
            ["Talep No"] = nameof(InventoryAsset.TalepNo),
        };

        public InventoryImportService(OrchestraDbContext db, ILogger<InventoryImportService> log)
        {
            _db = db;
            _log = log;
        }

        public async Task<InventoryImportResult> ImportAsync(
            Stream xlsxStream,
            string fileName,
            long fileSize,
            string? importedBy,
            CancellationToken ct = default)
        {
            var result = new InventoryImportResult();

            var batch = new InventoryImportBatch
            {
                FileName = fileName,
                FileSizeBytes = fileSize,
                ImportedBy = importedBy,
                ImportedAt = DateTime.UtcNow,
                Status = "Running",
            };
            _db.InventoryImportBatches.Add(batch);
            await _db.SaveChangesAsync(ct);
            result.BatchId = batch.Id;

            try
            {
                using var wb = new XLWorkbook(xlsxStream);
                var ws = wb.Worksheets.First();

                // Header satirini bul: "Asset Name" iceren satir
                var (headerRow, colMap) = FindHeader(ws);
                if (headerRow < 0)
                    throw new InvalidOperationException("Excel'de 'Asset Name' baslikli satir bulunamadi.");

                // Mevcut mapping cache (entity'leri tut — null cache'i guncelleyecegiz)
                var mappings = await _db.StoreNameMappings
                    .ToDictionaryAsync(m => m.RawName, m => m, StringComparer.OrdinalIgnoreCase, ct);

                // StoreDevices'tan magaza adlari (smart matcher icin)
                var storeCandidates = await _db.StoreDevices
                    .Where(s => !string.IsNullOrEmpty(s.StoreName) && s.StoreCode > 0)
                    .Select(s => new { s.StoreCode, s.StoreName })
                    .Distinct()
                    .ToListAsync(ct);
                var candidates = storeCandidates
                    .Select(s => (code: s.StoreCode, name: s.StoreName!))
                    .ToList();

                // Mevcut asset'leri AssetName -> entity ile cache'le (upsert icin)
                var existingAssets = await _db.InventoryAssets
                    .ToDictionaryAsync(a => a.AssetName, a => a, StringComparer.OrdinalIgnoreCase, ct);

                var unmatchedStores = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                var lastRow = ws.LastRowUsed()?.RowNumber() ?? headerRow;
                var toInsert = new List<InventoryAsset>();

                for (int r = headerRow + 1; r <= lastRow; r++)
                {
                    ct.ThrowIfCancellationRequested();
                    var row = ws.Row(r);
                    if (row.IsEmpty()) continue;

                    var assetNameRaw = GetCell(row, colMap, nameof(InventoryAsset.AssetName));
                    if (string.IsNullOrWhiteSpace(assetNameRaw)) continue;
                    var assetName = assetNameRaw.Trim();

                    result.TotalRows++;

                    var storeNameRaw = GetCell(row, colMap, nameof(InventoryAsset.StoreNameRaw))?.Trim();
                    int? storeCode = null;
                    bool autoMatched = false;

                    if (!string.IsNullOrWhiteSpace(storeNameRaw))
                    {
                        if (mappings.TryGetValue(storeNameRaw, out var existing))
                        {
                            if (existing.StoreCode != null)
                            {
                                storeCode = existing.StoreCode;
                            }
                            else
                            {
                                // Onceden unmatched cache'lenmis — yeniden auto-match dene
                                storeCode = ResolveAuto(storeNameRaw, candidates);
                                if (storeCode != null)
                                {
                                    existing.StoreCode = storeCode;
                                    existing.AutoMatched = true;
                                    existing.UpdatedAt = DateTime.UtcNow;
                                }
                                else
                                {
                                    unmatchedStores.Add(storeNameRaw);
                                }
                            }
                        }
                        else
                        {
                            storeCode = ResolveAuto(storeNameRaw, candidates);
                            if (storeCode != null) autoMatched = true;
                            else unmatchedStores.Add(storeNameRaw);

                            var newMapping = new StoreNameMapping
                            {
                                RawName = storeNameRaw,
                                StoreCode = storeCode,
                                AutoMatched = autoMatched,
                                CreatedAt = DateTime.UtcNow,
                                UpdatedAt = DateTime.UtcNow,
                            };
                            _db.StoreNameMappings.Add(newMapping);
                            mappings[storeNameRaw] = newMapping;
                        }
                    }

                    var isUpdate = existingAssets.TryGetValue(assetName, out var asset);
                    if (!isUpdate)
                    {
                        asset = new InventoryAsset { AssetName = assetName, CreatedAt = DateTime.UtcNow };
                    }

                    asset!.StoreNameRaw = storeNameRaw;
                    asset.StoreCode = storeCode;
                    asset.Department = NullIfNa(GetCell(row, colMap, nameof(InventoryAsset.Department)));
                    asset.ProductType = NullIfNa(GetCell(row, colMap, nameof(InventoryAsset.ProductType)));
                    asset.Product = NullIfNa(GetCell(row, colMap, nameof(InventoryAsset.Product)));
                    asset.ProductCode = NullIfNa(GetCell(row, colMap, nameof(InventoryAsset.ProductCode)));
                    asset.OrgSerialNumber = NullIfNa(GetCell(row, colMap, nameof(InventoryAsset.OrgSerialNumber)));
                    asset.ComputerName = NullIfNa(GetCell(row, colMap, nameof(InventoryAsset.ComputerName)));
                    asset.MacAddress = NullIfNa(GetCell(row, colMap, nameof(InventoryAsset.MacAddress)));
                    asset.AssetTag = NullIfNa(GetCell(row, colMap, nameof(InventoryAsset.AssetTag)));
                    asset.AcquisitionDate = ParseDate(GetCell(row, colMap, nameof(InventoryAsset.AcquisitionDate)));
                    asset.ExpiryDate = ParseDate(GetCell(row, colMap, nameof(InventoryAsset.ExpiryDate)));
                    asset.YazarkasaSicilNo = NullIfNa(GetCell(row, colMap, nameof(InventoryAsset.YazarkasaSicilNo)));
                    asset.BaseSeriNo = NullIfNa(GetCell(row, colMap, nameof(InventoryAsset.BaseSeriNo)));
                    asset.PrinterSeriNo = NullIfNa(GetCell(row, colMap, nameof(InventoryAsset.PrinterSeriNo)));
                    asset.IkinciMonitorSeriNo = NullIfNa(GetCell(row, colMap, nameof(InventoryAsset.IkinciMonitorSeriNo)));
                    asset.IpAddress = NullIfNa(GetCell(row, colMap, nameof(InventoryAsset.IpAddress)));
                    asset.AssetState = NullIfNa(GetCell(row, colMap, nameof(InventoryAsset.AssetState)));
                    asset.FizikselDurum = NullIfNa(GetCell(row, colMap, nameof(InventoryAsset.FizikselDurum)));
                    asset.PurchaseCost = ParseDecimal(GetCell(row, colMap, nameof(InventoryAsset.PurchaseCost)));
                    asset.FaturaNo = NullIfNa(GetCell(row, colMap, nameof(InventoryAsset.FaturaNo)));
                    asset.TalepNo = NullIfNa(GetCell(row, colMap, nameof(InventoryAsset.TalepNo)));
                    asset.ImportBatchId = batch.Id;
                    asset.ImportedAt = DateTime.UtcNow;
                    asset.UpdatedAt = DateTime.UtcNow;

                    if (isUpdate)
                    {
                        result.UpdatedCount++;
                    }
                    else
                    {
                        toInsert.Add(asset);
                        existingAssets[assetName] = asset;
                        result.InsertedCount++;
                    }
                }

                if (toInsert.Count > 0)
                {
                    _db.InventoryAssets.AddRange(toInsert);
                }

                result.UnmatchedStoreCount = unmatchedStores.Count;
                result.UnmatchedStoreNames = unmatchedStores.ToList();

                batch.TotalRows = result.TotalRows;
                batch.InsertedCount = result.InsertedCount;
                batch.UpdatedCount = result.UpdatedCount;
                batch.SkippedCount = result.SkippedCount;
                batch.UnmatchedStoreCount = result.UnmatchedStoreCount;
                batch.Status = "Completed";

                await _db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Inventory import failed (batch {BatchId})", batch.Id);
                batch.Status = "Failed";
                batch.ErrorMessage = ex.Message;
                await _db.SaveChangesAsync(ct);
                result.Errors.Add(ex.Message);
                throw;
            }

            return result;
        }

        /// <summary>
        /// Mevcut null mapping kayitlarini yeniden eslestirmeye calisir; basarili olanlari
        /// gunceller ve ilgili asset'lerin StoreCode'unu set eder. Sonuc: kac mapping ve
        /// kac asset guncellendigi.
        /// </summary>
        public async Task<(int updatedMappings, int updatedAssets)> RematchUnmappedAsync(CancellationToken ct = default)
        {
            var storeCandidates = await _db.StoreDevices
                .Where(s => !string.IsNullOrEmpty(s.StoreName) && s.StoreCode > 0)
                .Select(s => new { s.StoreCode, s.StoreName })
                .Distinct()
                .ToListAsync(ct);
            var candidates = storeCandidates.Select(s => (code: s.StoreCode, name: s.StoreName!)).ToList();

            var nullMappings = await _db.StoreNameMappings
                .Where(m => m.StoreCode == null)
                .ToListAsync(ct);

            int updatedMappings = 0;
            int updatedAssets = 0;
            var now = DateTime.UtcNow;

            foreach (var m in nullMappings)
            {
                ct.ThrowIfCancellationRequested();
                var code = ResolveAuto(m.RawName, candidates);
                if (code == null) continue;

                m.StoreCode = code;
                m.AutoMatched = true;
                m.UpdatedAt = now;
                updatedMappings++;

                var affected = await _db.InventoryAssets
                    .Where(a => a.StoreNameRaw == m.RawName && a.StoreCode == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.StoreCode, code), ct);
                updatedAssets += affected;
            }

            await _db.SaveChangesAsync(ct);
            return (updatedMappings, updatedAssets);
        }

        // Smart matcher: normalize + token-altkume + alt-dizi (kisaltma).
        // Birden fazla aday tutarsa null doner (ambigu).
        private static int? ResolveAuto(string raw, List<(int code, string name)> candidates)
        {
            return StoreNameMatcher.TryMatch(raw, candidates);
        }

        // ---------- helpers ----------

        private static (int row, Dictionary<string, int> colMap) FindHeader(IXLWorksheet ws)
        {
            var lastRow = Math.Min(ws.LastRowUsed()?.RowNumber() ?? 0, 30);
            for (int r = 1; r <= lastRow; r++)
            {
                var row = ws.Row(r);
                var lastCol = row.LastCellUsed()?.Address.ColumnNumber ?? 0;
                if (lastCol < 5) continue;

                var foundAssetName = false;
                var map = new Dictionary<string, int>();
                for (int c = 1; c <= lastCol; c++)
                {
                    var v = row.Cell(c).GetString().Trim();
                    if (string.IsNullOrEmpty(v)) continue;

                    if (HeaderMap.TryGetValue(v, out var prop))
                    {
                        map[prop] = c;
                        if (prop == nameof(InventoryAsset.AssetName)) foundAssetName = true;
                    }
                }

                if (foundAssetName) return (r, map);
            }
            return (-1, new Dictionary<string, int>());
        }

        private static string? GetCell(IXLRow row, Dictionary<string, int> colMap, string prop)
        {
            if (!colMap.TryGetValue(prop, out var c)) return null;
            var s = row.Cell(c).GetString();
            return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        }

        private static string? NullIfNa(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            if (string.Equals(s, "Not Assigned", StringComparison.OrdinalIgnoreCase)) return null;
            return s;
        }

        private static DateTime? ParseDate(string? s)
        {
            s = NullIfNa(s);
            if (s is null) return null;

            // SDP formati: "13.04.2026 15:00" veya "13.04.2026"
            var formats = new[]
            {
                "dd.MM.yyyy HH:mm", "dd.MM.yyyy H:mm", "dd.MM.yyyy",
                "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd",
            };
            if (DateTime.TryParseExact(s, formats, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal, out var dt))
                return dt.ToUniversalTime();

            if (DateTime.TryParse(s, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal, out dt))
                return dt.ToUniversalTime();

            return null;
        }

        // ---------- store name matching ----------

        private static class StoreNameMatcher
        {
            // Anlam tasimayan ek kelimeler — token kume karsilastirmasinda atlanir
            private static readonly HashSet<string> SuffixWords = new(StringComparer.Ordinal)
            {
                "giyim", "home", "kadin", "kadın", "erkek", "outlet", "magaza", "mağaza",
                "avm", "plaza", "park", "store", "ev", "kids", "junior", "men", "women",
            };

            private static readonly CultureInfo Tr = new("tr-TR");

            public static int? TryMatch(string raw, List<(int code, string name)> candidates)
            {
                var rawNorm = Normalize(raw);
                if (string.IsNullOrEmpty(rawNorm) || candidates.Count == 0) return null;

                var prepared = candidates
                    .Select(c => (c.code, norm: Normalize(c.name)))
                    .Where(c => !string.IsNullOrEmpty(c.norm))
                    .ToList();

                // 1) Tam normalize esitlik
                var exact = prepared.Where(c => c.norm == rawNorm).Select(c => c.code).Distinct().ToList();
                if (exact.Count == 1) return exact[0];
                if (exact.Count > 1) return null;

                var rawTokens = SignificantTokens(rawNorm);
                if (rawTokens.Count == 0) return null;

                // 2) Token uyumu: raw'in tum anlamli token'lari aday isimde karsiligi olmali
                var matched = new HashSet<int>();
                foreach (var c in prepared)
                {
                    var candTokens = SignificantTokens(c.norm);
                    if (candTokens.Count == 0) continue;
                    var ok = rawTokens.All(rt => candTokens.Any(ct => TokenCompatible(rt, ct)));
                    if (ok) matched.Add(c.code);
                }

                return matched.Count == 1 ? matched.First() : null;
            }

            public static string Normalize(string s)
            {
                if (string.IsNullOrEmpty(s)) return "";
                var lower = s.ToLower(Tr);
                var sb = new StringBuilder(lower.Length);
                foreach (var ch in lower)
                {
                    if (char.IsLetterOrDigit(ch)) sb.Append(ch);
                    else sb.Append(' ');
                }
                return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
            }

            private static List<string> SignificantTokens(string normalized)
            {
                return normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Where(t => t.Length >= 2 && !SuffixWords.Contains(t))
                    .ToList();
            }

            private static bool TokenCompatible(string a, string b)
            {
                if (a == b) return true;
                // Prefix (en az 3 karakter)
                if (a.Length >= 3 && b.StartsWith(a, StringComparison.Ordinal)) return true;
                if (b.Length >= 3 && a.StartsWith(b, StringComparison.Ordinal)) return true;
                // Alt-dizi (kisaltmalar icin: "ckale" -> "canakkale")
                // Cok kisa subsequence false-positive uretmesin diye min uzunluk 4
                if (a.Length >= 4 && a.Length * 2 >= b.Length && IsSubsequence(a, b)) return true;
                if (b.Length >= 4 && b.Length * 2 >= a.Length && IsSubsequence(b, a)) return true;
                return false;
            }

            private static bool IsSubsequence(string sub, string full)
            {
                int i = 0;
                foreach (var c in full)
                {
                    if (i < sub.Length && c == sub[i]) i++;
                    if (i == sub.Length) return true;
                }
                return i == sub.Length;
            }
        }

        private static decimal? ParseDecimal(string? s)
        {
            s = NullIfNa(s);
            if (s is null) return null;
            // " TRY1,318.00" gibi -> sayi disindaki karakterleri temizle
            var cleaned = Regex.Replace(s, @"[^\d.,\-]", "");
            // SDP en-US bicimi: virgul=binlik, nokta=ondalik
            cleaned = cleaned.Replace(",", "");
            if (decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var d))
                return d;
            return null;
        }
    }
}
