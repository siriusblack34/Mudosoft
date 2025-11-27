using MudoSoft.Backend.Models;
using System.Collections.Generic;

namespace MudoSoft.Backend.Services;

public interface IDeviceRepository
{
    List<Device> GetAll();
    Device? GetById(string id);
    void Add(Device device);
    void Update(Device device);
    
    // HATA ÇÖZÜMÜ: DiscoveryWorker için SaveAll metodu eklendi
    void SaveAll(IEnumerable<Device> devices);
}