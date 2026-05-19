using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Data;

namespace Orchestra.Backend.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/activity-log")]
    public class ActivityLogController : ControllerBase
    {
        private readonly OrchestraDbContext _db;

        public ActivityLogController(OrchestraDbContext db) => _db = db;

        [HttpGet]
        public async Task<IActionResult> Get(
            [FromQuery] string? category,
            [FromQuery] string? username,
            [FromQuery] bool? successOnly,
            [FromQuery] bool? failuresOnly,
            [FromQuery] string? search,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 100,
            CancellationToken ct = default)
        {
            if (page < 1) page = 1;
            if (pageSize < 1 || pageSize > 500) pageSize = 100;

            var q = _db.ActivityLogs.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(category))
                q = q.Where(a => a.Category == category);

            if (!string.IsNullOrWhiteSpace(username))
                q = q.Where(a => a.Username == username);

            if (successOnly == true)
                q = q.Where(a => a.Success);

            if (failuresOnly == true)
                q = q.Where(a => !a.Success);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var like = $"%{search.Trim()}%";
                q = q.Where(a =>
                    EF.Functions.ILike(a.Action, like) ||
                    (a.Target != null && EF.Functions.ILike(a.Target, like)) ||
                    (a.Details != null && EF.Functions.ILike(a.Details, like)) ||
                    (a.ErrorMessage != null && EF.Functions.ILike(a.ErrorMessage, like)));
            }

            var total = await q.CountAsync(ct);
            var items = await q
                .OrderByDescending(a => a.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);

            return Ok(new { items, total, page, pageSize });
        }

        [HttpGet("categories")]
        public async Task<IActionResult> GetCategories(CancellationToken ct)
        {
            var cats = await _db.ActivityLogs
                .GroupBy(a => a.Category)
                .Select(g => new { name = g.Key, count = g.Count() })
                .OrderByDescending(x => x.count)
                .ToListAsync(ct);
            return Ok(cats);
        }
    }
}
