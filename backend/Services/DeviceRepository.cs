using System.Text.Json;
using MudoSoft.Backend.Models;

namespace MudoSoft.Backend.Services;

public interface IDeviceRepository
{
    List<Device> GetAll();
    Device? GetById(string id);
    void SaveAll(List<Device> devices);
}

public class DeviceRepository : IDeviceRepository
{
    private readonly string _devicesPath;
    private readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public DeviceRepository(IConfiguration config)
    {
        var dataDir = config["MudoSoft:DataDirectory"] 
                      ?? "C:\\MudoSoft\\data";

        Directory.CreateDirectory(dataDir);

        _devicesPath = Path.Combine(dataDir, "devices.json");

        // ❗ Artık seed cihaz yok, boş liste ile başlar
        if (!File.Exists(_devicesPath))
        {
            File.WriteAllText(_devicesPath, "[]");
        }
    }

    public List<Device> GetAll()
    {
        if (!File.Exists(_devicesPath))
            return new List<Device>();

        var json = File.ReadAllText(_devicesPath);
        return JsonSerializer.Deserialize<List<Device>>(json, _options) ?? new List<Device>();
    }

    public Device? GetById(string id)
    {
        return GetAll().FirstOrDefault(d => d.Id == id);
    }

    public void SaveAll(List<Device> devices)
    {
        var json = JsonSerializer.Serialize(devices, _options);
        File.WriteAllText(_devicesPath, json);
    }
}
