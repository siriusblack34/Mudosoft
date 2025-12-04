// backend/Services/StoreDiscoveryService.cs (HATA GİDERİLDİ VE KK3 VERSİYONU)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MudoSoft.Backend.Data; // FIX: DbContext için eklendi.
using MudoSoft.Backend.Models; // FIX: StoreDevice modeli için eklendi.

namespace MudoSoft.Backend.Services
{
    public class StoreDiscoveryService : IStoreDiscoveryService
    {
        private readonly MudoSoftDbContext _dbContext;
        
        private const string DbUser = "GENIUS3"; 
        private const string DbPass = "GENIUSOPEN"; 

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
                "KK3" => 33, // GÜNCELLEME: KK13 -> KK3
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
                        var deviceName = $"{storeCode:000}-{type}-{storeName.Replace(' ', '_').Replace(':', '_')}"; 
                        
                        var existingDevice = await _dbContext.StoreDevices
                            .FirstOrDefaultAsync(d => d.StoreCode == storeCode && d.DeviceType == type);
                        
                        var connectionString = GetSecureConnectionString(ip); 

                        if (existingDevice != null)
                        {
                            existingDevice.CalculatedIpAddress = ip;
                            existingDevice.StoreName = storeName;
                            existingDevice.DeviceName = deviceName; 
                            existingDevice.DbConnectionString = connectionString;
                            existingDevice.LastSyncDate = DateTimeOffset.UtcNow;
                        }
                        else
                        {
                            _dbContext.StoreDevices.Add(new StoreDevice
                            {
                                StoreCode = storeCode,
                                StoreName = storeName,
                                DeviceType = type,
                                CalculatedIpAddress = ip,
                                DeviceName = deviceName, 
                                DbConnectionString = connectionString
                            });
                        }
                    }
                    catch (ArgumentException)
                    {
                        // Hatalı cihaz tiplerini atla
                    }
                }
            }
            await _dbContext.SaveChangesAsync();
        }
        
        public async Task<List<StoreDevice>> GetAvailableDevicesAsync()
        {
            return await _dbContext.StoreDevices.AsNoTracking().OrderBy(d => d.StoreCode).ToListAsync();
        }
    }
}