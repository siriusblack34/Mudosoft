using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MudoSoft.Backend.Data;
using MudoSoft.Backend.Models;
using MudoSoft.Backend.Services;

namespace MudoSoft.Backend.Controllers
{
    [ApiController]
    [Route("api/sqlquery")]
    public class SqlQueryController : ControllerBase
    {
        private readonly IRemoteSqlService _remoteSqlService;
        private readonly MudoSoftDbContext _db;
        private readonly ILogger<SqlQueryController> _logger;

        public SqlQueryController(
            IRemoteSqlService remoteSqlService,
            MudoSoftDbContext db,
            ILogger<SqlQueryController> logger)
        {
            _remoteSqlService = remoteSqlService;
            _db = db;
            _logger = logger;
        }

        // ===========================================================
        // 0) TÜM CİHAZLAR (ONLINE+OFFLINE)
        // ===========================================================
        [HttpGet("devices/all")]
        public async Task<IActionResult> GetAllDevices()
        {
            var devices = await _db.StoreDevices
                .AsNoTracking()
                .Select(d => new
                {
                    d.DeviceId,
                    d.StoreCode,
                    d.StoreName,
                    d.DeviceType,
                    d.CalculatedIpAddress
                })
                .ToListAsync();

            return Ok(devices);
        }

        // ===========================================================
        // 1) ONLINE CİHAZLAR (PORT TEST 1433)
        // ===========================================================
        [HttpGet("devices/online-fast")]
        public async Task<IActionResult> GetFastOnlineDevices(
            [FromServices] FastSqlReachabilityService fastCheck)
        {
            var devices = await _db.StoreDevices
                .AsNoTracking()
                .ToListAsync();

            var onlineList = new List<object>();

            await Task.WhenAll(devices.Select(async d =>
            {
                bool ok = await fastCheck.IsSqlReachable(d.CalculatedIpAddress, 1433);

                if (ok)
                {
                    onlineList.Add(new
                    {
                        d.DeviceId,
                        d.StoreCode,
                        d.StoreName,
                        d.DeviceType,
                        d.CalculatedIpAddress
                    });
                }
            }));

            return Ok(onlineList);
        }

        // ===========================================================
        // 2) SQL SORGUSU ÇALIŞTIR
        // ===========================================================
        [HttpPost("execute")]
        public async Task<IActionResult> ExecuteQuery([FromBody] ExecuteSqlQueryRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(new { error = "ModelState invalid" });

            var device = await _db.StoreDevices
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.DeviceId == request.DeviceId);

            if (device == null)
                return NotFound(new { error = "Device not found" });

            try
            {
                var json = await _remoteSqlService.ExecuteQueryAndReturnJsonAsync(
                    device.DbConnectionString, request.Query);

                var result = System.Text.Json.JsonSerializer.Deserialize<object>(json);
                return Ok(result);
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "SQL error");
                return BadRequest(new { error = ex.Message });
            }
        }
    }
}
