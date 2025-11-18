using MudoSoft.Backend.Models;

namespace MudoSoft.Backend.Services;

public interface IAlertRepository
{
    List<Alert> GetAll();
    void Add(Alert alert);
}

public class AlertRepository : IAlertRepository
{
    private readonly List<Alert> _alerts = new();

    public List<Alert> GetAll() => _alerts;

    public void Add(Alert alert)
    {
        _alerts.Add(alert);
    }
}