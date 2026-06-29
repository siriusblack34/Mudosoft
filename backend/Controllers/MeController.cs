using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orchestra.Backend.Services;

namespace Orchestra.Backend.Controllers;

/// <summary>
/// Giriş yapan kullanıcının kendine ait bilgileri. Etkin menü erişimi burada hesaplanır.
/// </summary>
[ApiController]
[Authorize]
[Route("api/me")]
public class MeController : ControllerBase
{
    private readonly IMenuAccessService _menuAccess;

    public MeController(IMenuAccessService menuAccess) => _menuAccess = menuAccess;

    /// <summary>
    /// Kullanıcının etkin menü erişiminin ham bileşenleri. Frontend bunları kendi
    /// menü kataloğuna göre çözer. Admin için isAdmin=true döner (her şeyi görür).
    /// </summary>
    [HttpGet("menus")]
    public async Task<IActionResult> GetMyMenus()
    {
        var isAdmin = User.IsInRole("Admin");
        var access = await _menuAccess.GetForUserAsync(User.Identity?.Name, isAdmin);

        return Ok(new
        {
            isAdmin = access.IsAdmin,
            allowAllByDefault = access.AllowAllByDefault,
            profileName = access.ProfileName,
            profileAllowed = access.ProfileAllowed,
            profileHidden = access.ProfileHidden,
            grants = access.Grants,
            denials = access.Denials
        });
    }
}
