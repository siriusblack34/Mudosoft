// backend/Services/IStoreDiscoveryService.cs

using System.Collections.Generic;
using System.Threading.Tasks;
using MudoSoft.Backend.Models; 

namespace MudoSoft.Backend.Services
{
    public interface IStoreDiscoveryService
    {
        Task SyncStoreDevicesFromCsvAsync(string csvContent);
        Task<List<StoreDevice>> GetAvailableDevicesAsync();
    }
}