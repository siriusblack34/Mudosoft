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
    
    // 🏆 GÜNCELLEME: Tüm cihazlar çekilirken sadece eski (var olan) sütunlar çekilir.
    // Bu, SQL hatasını atlar.
    public List<Device> GetAll()
    {
        return _context.Devices
            .Include(d => d.Metrics) // Metrics koleksiyonunu yükle
            .Select(d => new Device 
            {
                // 🔥 SADECE VAR OLAN ESKİ SÜTUNLAR ÇEKİLİYOR
                Id = d.Id,
                Hostname = d.Hostname,
                IpAddress = d.IpAddress,
                StoreCode = d.StoreCode,
                StoreName = d.StoreName,
                Type = d.Type,
                Os = d.Os,
                SqlVersion = d.SqlVersion,
                PosVersion = d.PosVersion,
                AgentVersion = d.AgentVersion,
                Online = d.Online,
                FirstSeen = d.FirstSeen,
                LastSeen = d.LastSeen,
                HealthStatus = d.HealthStatus,
                HealthScore = d.HealthScore,
                Metrics = d.Metrics.ToList() // İlişkili veriler çekilmeye devam eder
                // YENİ Current* Sütunları BURADA YOK
            })
            .ToList();
    }

    // 🏆 GÜNCELLEME: Tek cihaz çekilirken de sadece eski (var olan) sütunlar çekilir.
    public Device? GetById(string id)
    {
        return _context.Devices
            .Include(d => d.Metrics) // Metrics koleksiyonunu yükle
            .Select(d => new Device 
            {
                // 🔥 SADECE VAR OLAN ESKİ SÜTUNLAR ÇEKİLİYOR
                Id = d.Id,
                Hostname = d.Hostname,
                IpAddress = d.IpAddress,
                StoreCode = d.StoreCode,
                StoreName = d.StoreName,
                Type = d.Type,
                Os = d.Os,
                SqlVersion = d.SqlVersion,
                PosVersion = d.PosVersion,
                AgentVersion = d.AgentVersion,
                Online = d.Online,
                FirstSeen = d.FirstSeen,
                LastSeen = d.LastSeen,
                HealthStatus = d.HealthStatus,
                HealthScore = d.HealthScore,
                Metrics = d.Metrics.ToList() // İlişkili veriler çekilmeye devam eder
                // YENİ Current* Sütunları BURADA YOK
            })
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