// backend/Services/StoreDiscoveryService.cs (FINAL – PK FIX, DUPLICATE FIX, STABLE)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MudoSoft.Backend.Data;
using MudoSoft.Backend.Models;

namespace MudoSoft.Backend.Services
{
    public class StoreDiscoveryService : IStoreDiscoveryService
    {
        private readonly MudoSoftDbContext _dbContext;

        private const string DbUser = "GENIUS3";
        private const string DbPass = "***REMOVED***";

        public StoreDiscoveryService(MudoSoftDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        private string CalculateIpAddress(int storeCode, string deviceType)
        {
            if (storeCode == 93)
            {
                if (deviceType == "PC") return "192.168.125.237";
                if (deviceType.StartsWith("KK")) return "192.168.125.235";
            }

            var lastOctet = deviceType switch
            {
                "PC" => 2,
                "KK1" => 31,
                "KK2" => 32,
                "KK3" => 33,
                _ => throw new ArgumentException($"Geçersiz cihaz tipi: {deviceType}")
            };

            return $"192.168.{storeCode}.{lastOctet}";
        }

        private string GetSecureConnectionString(string ip, string dbName = "Genius3")
        {
            return $"Data Source={ip};Initial Catalog={dbName};User ID={DbUser};Password={DbPass};Encrypt=False;TrustServerCertificate=True;Connect Timeout=5";
        }

        public async Task SyncStoreDevicesFromCsvAsync(string csvContent)
        {
            var lines = csvContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                  .Skip(1);

            var deviceTypes = new[] { "PC", "KK1", "KK2", "KK3" };

            foreach (var line in lines)
            {
                var parts = line.Split(',');
                if (parts.Length < 2) continue;

                if (!int.TryParse(parts[0].Trim(), out var storeCode)) continue;
                var storeName = parts[1].Trim();

                foreach (var type in deviceTypes)
                {
                    try
                    {
                        var ip = CalculateIpAddress(storeCode, type);
                        var connectionString = GetSecureConnectionString(ip);
                        var deviceName = $"{storeCode:000}-{type}-{storeName.Replace(' ', '_').Replace(':', '_')}";
                        
                        // ➤ KRİTİK: Benzersiz Primary Key üret
                        var deviceId = $"{storeCode}-{type}";

                        // Var mı diye kontrol
                        var existing = await _dbContext.StoreDevices
                            .AsNoTracking()
                            .FirstOrDefaultAsync(d => d.DeviceId == deviceId);

                        if (existing == null)
                        {
                            // Yeni ekle
                            _dbContext.StoreDevices.Add(new StoreDevice
                            {
                                DeviceId = deviceId,
                                StoreCode = storeCode,
                                StoreName = storeName,
                                DeviceType = type,
                                CalculatedIpAddress = ip,
                                DeviceName = deviceName,
                                DbConnectionString = connectionString,
                                LastSyncDate = DateTimeOffset.UtcNow
                            });
                        }
                        else
                        {
                            // Güncelleme modunda tekrar track etme!
                            existing.StoreCode = storeCode;
                            existing.StoreName = storeName;
                            existing.DeviceType = type;
                            existing.CalculatedIpAddress = ip;
                            existing.DeviceName = deviceName;
                            existing.DbConnectionString = connectionString;
                            existing.LastSyncDate = DateTimeOffset.UtcNow;

                            _dbContext.StoreDevices.Update(existing);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[StoreDiscovery] Hata: {ex.Message}");
                    }
                }
            }

            await _dbContext.SaveChangesAsync();
        }

        public async Task<List<StoreDevice>> GetAvailableDevicesAsync()
        {
            return await _dbContext.StoreDevices
                .AsNoTracking()
                .OrderBy(d => d.StoreCode)
                .ToListAsync();
        }
    }
}
