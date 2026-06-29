using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Orchestra.Backend.Services;

namespace Orchestra.Backend.Middleware;

/// <summary>
/// Bir controller/action'ı belirli bir menü path'ine bağlar. Admin her zaman geçer.
/// Diğer kullanıcılar yalnızca etkin menülerinde bu path varsa erişebilir; aksi halde 403.
/// Route guard'ı bypass edip API'yi doğrudan çağırmayı da engeller.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequireMenuAttribute : Attribute, IAsyncAuthorizationFilter
{
    private readonly string _menuPath;

    public RequireMenuAttribute(string menuPath) => _menuPath = menuPath;

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var principal = context.HttpContext.User;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        if (principal.IsInRole("Admin")) return; // lord — her şeye erişir

        var svc = context.HttpContext.RequestServices.GetRequiredService<IMenuAccessService>();
        var access = await svc.GetForUserAsync(principal.Identity?.Name, isAdmin: false);

        if (!svc.CanAccess(access, _menuPath))
        {
            context.Result = new ObjectResult(new { error = "Bu işlem için yetkiniz yok" })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }
    }
}
