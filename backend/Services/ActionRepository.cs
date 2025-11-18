using System.Text.Json;
using MudoSoft.Backend.Models;

namespace MudoSoft.Backend.Services;

public interface IActionRepository
{
    List<ActionRecord> GetAll();
    List<ActionRecord> GetByDevice(string deviceId);
    ActionRecord Add(ActionRecord record);
}

public class ActionRepository : IActionRepository
{
    private readonly string _actionsPath;
    private readonly JsonSerializerOptions _options = new() { WriteIndented = true };

    public ActionRepository(IConfiguration configuration)
    {
        var dataDir = configuration["MudoSoft:DataDirectory"]
                      ?? "C:\\MudoSoft\\data";
        Directory.CreateDirectory(dataDir);
        _actionsPath = Path.Combine(dataDir, "actions.json");

        if (!File.Exists(_actionsPath))
        {
            SaveAll(new List<ActionRecord>());
        }
    }

    public List<ActionRecord> GetAll()
    {
        var json = File.ReadAllText(_actionsPath);
        return JsonSerializer.Deserialize<List<ActionRecord>>(json, _options)
               ?? new List<ActionRecord>();
    }

    public List<ActionRecord> GetByDevice(string deviceId)
    {
        return GetAll().Where(a => a.DeviceId == deviceId).ToList();
    }

    public ActionRecord Add(ActionRecord record)
    {
        var all = GetAll();
        all.Insert(0, record);
        SaveAll(all);
        return record;
    }

    private void SaveAll(List<ActionRecord> actions)
    {
        var json = JsonSerializer.Serialize(actions, _options);
        File.WriteAllText(_actionsPath, json);
    }
}
