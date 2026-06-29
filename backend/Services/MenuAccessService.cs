using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Data;

namespace Orchestra.Backend.Services;

/// <summary>
/// Bir kullanıcının etkin menü erişiminin ham bileşenleri. Frontend bu alanları kendi
/// menü kataloğuna (navGroups) karşı çözer; backend ise tekil path kontrolünde kullanır.
/// </summary>
public class EffectiveMenuAccess
{
    public bool IsAdmin { get; set; }
    public bool AllowAllByDefault { get; set; }
    public List<string> ProfileAllowed { get; set; } = new();
    public List<string> ProfileHidden { get; set; } = new();
    public List<string> Grants { get; set; } = new();
    public List<string> Denials { get; set; } = new();
    public string? ProfileName { get; set; }
}

public interface IMenuAccessService
{
    /// <summary>Kullanıcının etkin menü erişimini (profil + kişisel override) hesaplar.</summary>
    Task<EffectiveMenuAccess> GetForUserAsync(string? username, bool isAdmin);

    /// <summary>Tek bir menü path'ine erişim var mı? (backend [RequireMenu] kontrolü için)</summary>
    bool CanAccess(EffectiveMenuAccess access, string menuPath);
}

public class MenuAccessService : IMenuAccessService
{
    private readonly OrchestraDbContext _db;

    public MenuAccessService(OrchestraDbContext db) => _db = db;

    public async Task<EffectiveMenuAccess> GetForUserAsync(string? username, bool isAdmin)
    {
        if (isAdmin)
            return new EffectiveMenuAccess { IsAdmin = true, AllowAllByDefault = true };

        var access = new EffectiveMenuAccess();

        if (string.IsNullOrWhiteSpace(username))
            return access; // kimlik yok → her şey kapalı (güvenli)

        var user = await _db.Users
            .AsNoTracking()
            .Include(u => u.MenuProfile)
            .FirstOrDefaultAsync(u => u.Username == username && u.IsActive);

        // DB'de normal kullanıcı kaydı yoksa (örn. agent token) erişim verme.
        if (user == null)
            return access;

        // Profil atanmamışsa sistem "Teknisyen" profiline düş.
        var profile = user.MenuProfile;
        if (profile == null)
        {
            profile = await _db.MenuProfiles
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.IsSystem && p.Name == "Teknisyen");
        }

        if (profile != null)
        {
            access.AllowAllByDefault = profile.AllowAllByDefault;
            access.ProfileAllowed = Parse(profile.AllowedMenusJson);
            access.ProfileHidden = Parse(profile.HiddenMenusJson);
            access.ProfileName = profile.Name;
        }

        access.Grants = Parse(user.MenuGrantsJson);
        access.Denials = Parse(user.MenuDenialsJson);
        return access;
    }

    public bool CanAccess(EffectiveMenuAccess access, string menuPath)
    {
        if (access.IsAdmin) return true;
        if (string.IsNullOrWhiteSpace(menuPath)) return true;

        var path = menuPath.Trim();

        // Kişisel override en yüksek öncelik.
        if (access.Denials.Contains(path)) return false;
        if (access.Grants.Contains(path)) return true;

        return access.AllowAllByDefault
            ? !access.ProfileHidden.Contains(path)
            : access.ProfileAllowed.Contains(path);
    }

    private static List<string> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? new();
        }
        catch
        {
            return new();
        }
    }
}
