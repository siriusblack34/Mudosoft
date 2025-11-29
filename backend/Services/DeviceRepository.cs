using Microsoft.EntityFrameworkCore;
using MudoSoft.Backend.Data;
using MudoSoft.Backend.Models;
using System.Collections.Generic;
using System.Linq;

namespace MudoSoft.Backend.Services;

public class DeviceRepository : IDeviceRepository
{
    private readonly MudoSoftDbContext _context;

    public DeviceRepository(MudoSoftDbContext context)
    {
        _context = context;
    }
    
    public List<Device> GetAll()
    {
        return _context.Devices
            .ToList();
    }

    // 🚀 OPTİMİZASYON: Yavaşlığa neden olan .Include(d => d.Metrics) KALDIRILDI.
    public Device? GetById(string id)
    {
        return _context.Devices
            .FirstOrDefault(d => d.Id == id);
    }

    public void Add(Device device)
    {
        _context.Devices.Add(device);
        _context.SaveChanges();
    }

    public void Update(Device device)
    {
        _context.Devices.Update(device);
        _context.SaveChanges();
    }

    public void SaveAll(IEnumerable<Device> devices)
    {
        // Mevcut cihazları kontrol et veya güncelle (Basit Upsert mantığı)
        foreach (var device in devices)
        {
            if (!_context.Devices.Any(d => d.Id == device.Id))
            {
                _context.Devices.Add(device);
            }
            // Güncelleme gerekirse buraya eklenebilir
        }
        _context.SaveChanges();
    }
}