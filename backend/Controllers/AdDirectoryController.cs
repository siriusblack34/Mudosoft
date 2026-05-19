using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orchestra.Backend.Services;

namespace Orchestra.Backend.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/ad")]
public class AdDirectoryController : ControllerBase
{
    private readonly ILdapDirectoryService _ad;
    private readonly ILogger<AdDirectoryController> _logger;

    public AdDirectoryController(ILdapDirectoryService ad, ILogger<AdDirectoryController> logger)
    {
        _ad = ad;
        _logger = logger;
    }

    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        return Ok(new { available = _ad.IsAvailable });
    }

    [HttpGet("ous")]
    public async Task<IActionResult> GetOus(CancellationToken ct)
    {
        if (!_ad.IsAvailable) return Problem("LDAP is not configured", statusCode: 503);
        try
        {
            var ous = await _ad.GetOuTreeAsync(ct);
            return Ok(new { count = ous.Count, ous });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetOus failed");
            return Problem(ex.Message, statusCode: 500);
        }
    }

    [HttpGet("computers")]
    public async Task<IActionResult> GetComputers([FromQuery] string? ou, [FromQuery] string? group,
        [FromQuery] bool recursive = false, CancellationToken ct = default)
    {
        if (!_ad.IsAvailable) return Problem("LDAP is not configured", statusCode: 503);
        if (string.IsNullOrWhiteSpace(ou) && string.IsNullOrWhiteSpace(group))
            return BadRequest(new { error = "ou veya group parametresi gerekli" });

        try
        {
            var computers = !string.IsNullOrWhiteSpace(group)
                ? await _ad.GetComputersInGroupAsync(group, ct)
                : await _ad.GetComputersInOuAsync(ou!, recursive, ct);
            return Ok(new { count = computers.Count, computers });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetComputers failed (ou={Ou}, group={Group})", ou, group);
            return Problem(ex.Message, statusCode: 500);
        }
    }

    [HttpGet("groups")]
    public async Task<IActionResult> GetGroups(CancellationToken ct)
    {
        if (!_ad.IsAvailable) return Problem("LDAP is not configured", statusCode: 503);
        try
        {
            var groups = await _ad.GetGroupsAsync(ct);
            return Ok(new { count = groups.Count, groups });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetGroups failed");
            return Problem(ex.Message, statusCode: 500);
        }
    }

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers([FromQuery] string? ou, [FromQuery] string? q,
        [FromQuery] bool recursive = false, [FromQuery] int limit = 500, CancellationToken ct = default)
    {
        if (!_ad.IsAvailable) return Problem("LDAP is not configured", statusCode: 503);
        if (string.IsNullOrWhiteSpace(ou) && string.IsNullOrWhiteSpace(q))
            return BadRequest(new { error = "ou veya q parametresi gerekli" });

        try
        {
            var users = !string.IsNullOrWhiteSpace(q)
                ? await _ad.SearchUsersAsync(q, Math.Clamp(limit, 1, 1000), ct)
                : await _ad.GetUsersInOuAsync(ou!, recursive, ct);
            return Ok(new { count = users.Count, users });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetUsers failed (ou={Ou}, q={Q})", ou, q);
            return Problem(ex.Message, statusCode: 500);
        }
    }

    [HttpPost("resolve")]
    public async Task<IActionResult> Resolve([FromBody] ResolveRequest req, CancellationToken ct)
    {
        if (req == null || string.IsNullOrWhiteSpace(req.Hostname))
            return BadRequest(new { error = "Hostname gerekli" });
        var ip = await _ad.ResolveHostnameAsync(req.Hostname.Trim(), ct);
        return Ok(new { hostname = req.Hostname, ip });
    }

    [HttpPost("probe")]
    public async Task<IActionResult> Probe([FromBody] ProbeRequest req, CancellationToken ct)
    {
        if (req?.Hostnames == null || req.Hostnames.Count == 0)
            return BadRequest(new { error = "Hostname listesi gerekli" });

        var tasks = req.Hostnames
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(async h =>
            {
                var hostname = h.Trim();
                string? ip = null;
                bool alive = false;
                string? error = null;
                try
                {
                    ip = await _ad.ResolveHostnameAsync(hostname, ct);
                    if (string.IsNullOrEmpty(ip))
                    {
                        error = "DNS çözümleme başarısız";
                    }
                    else
                    {
                        using var ping = new System.Net.NetworkInformation.Ping();
                        var reply = await ping.SendPingAsync(ip, 2000);
                        alive = reply.Status == System.Net.NetworkInformation.IPStatus.Success;
                        if (!alive) error = $"Ping: {reply.Status}";
                    }
                }
                catch (Exception ex) { error = ex.Message; }
                return new { hostname, ip, alive, error };
            });

        var results = await Task.WhenAll(tasks);
        return Ok(new { results });
    }

    public class ResolveRequest
    {
        public string Hostname { get; set; } = string.Empty;
    }

    public class ProbeRequest
    {
        public List<string> Hostnames { get; set; } = new();
    }
}
