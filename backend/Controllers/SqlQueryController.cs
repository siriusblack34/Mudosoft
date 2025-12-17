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
        private readonly FastSqlReachabilityService _fastCheck;

        public SqlQueryController(
            IRemoteSqlService remoteSqlService,
            MudoSoftDbContext db,
            FastSqlReachabilityService fastCheck,
            ILogger<SqlQueryController> logger)
        {
            _remoteSqlService = remoteSqlService;
            _db = db;
            _fastCheck = fastCheck;
            _logger = logger;
        }

        // ===========================================================
        // 0) TÜM CİHAZLAR (ENVANTER)
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
        // 1) TÜM CİHAZLAR + ONLINE / OFFLINE (TEK DOĞRU ENDPOINT)
        // ===========================================================
        [HttpGet("devices/with-status")]
        public async Task<IActionResult> GetDevicesWithStatus(
            [FromQuery] int timeoutMs = 500,
            [FromQuery] int maxConcurrency = 40)
        {
            var devices = await _db.StoreDevices
                .AsNoTracking()
                .Select(d => new StoreDeviceWithStatusDto
                {
                    DeviceId = d.DeviceId,
                    StoreCode = d.StoreCode,
                    StoreName = d.StoreName,
                    DeviceType = d.DeviceType,
                    DeviceName = d.DeviceName,
                    CalculatedIpAddress = d.CalculatedIpAddress,
                    IsOnline = false
                })
                .ToListAsync();

            using var sem = new SemaphoreSlim(Math.Max(1, maxConcurrency));

            var tasks = devices.Select(async d =>
            {
                await sem.WaitAsync();
                try
                {
                    d.IsOnline = await _fastCheck.IsSqlReachableAsync(
                        d.CalculatedIpAddress,
                        1433,
                        timeoutMs
                    );
                }
                finally
                {
                    sem.Release();
                }
            });

            await Task.WhenAll(tasks);

            return Ok(devices);
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
                    device.DbConnectionString,
                    request.Query
                );

                var result = System.Text.Json.JsonSerializer.Deserialize<object>(json);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SQL error");
                return BadRequest(new { error = ex.Message });
            }
        }
    }
}
