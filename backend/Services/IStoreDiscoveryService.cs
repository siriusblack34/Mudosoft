// backend/Services/IStoreDiscoveryService.cs

using System.Collections.Generic;
using System.Threading.Tasks;
using Orchestra.Backend.Models; 

namespace Orchestra.Backend.Services
{
    public interface IStoreDiscoveryService
    {
        Task SyncStoreDevicesFromCsvAsync(string csvContent);
        Task<List<StoreDevice>> GetAvailableDevicesAsync();
    }
}