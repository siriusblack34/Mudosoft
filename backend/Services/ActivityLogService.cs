using Microsoft.AspNetCore.Http;
using Orchestra.Backend.Data;
using Orchestra.Backend.Models;
using System.Security.Claims;

namespace Orchestra.Backend.Services
{
    public class ActivityLogService
    {
        private readonly OrchestraDbContext _db;
        private readonly IHttpContextAccessor _http;
        private readonly ILogger<ActivityLogService> _log;

        public ActivityLogService(OrchestraDbContext db, IHttpContextAccessor http, ILogger<ActivityLogService> log)
        {
            _db = db;
            _http = http;
            _log = log;
        }

        public Task LogAsync(string category, string action, string? target = null,
            string? details = null, bool success = true, string? error = null, CancellationToken ct = default)
        {
            try
            {
                var username = _http.HttpContext?.User?.FindFirstValue(ClaimTypes.Name)
                    ?? _http.HttpContext?.User?.Identity?.Name;

                _db.ActivityLogs.Add(new ActivityLog
                {
                    Username = username,
                    Category = category,
                    Action = action,
                    Target = target,
                    Details = details,
                    Success = success,
                    ErrorMessage = error,
                    CreatedAt = DateTime.UtcNow,
                });
                return _db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                // Audit log'un patlamasi asil islemi bozmasin
                _log.LogWarning(ex, "ActivityLog kaydi yazilirken hata: {Category}/{Action}", category, action);
                return Task.CompletedTask;
            }
        }
    }
}
