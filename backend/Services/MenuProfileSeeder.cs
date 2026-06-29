using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Data;
using Orchestra.Backend.Models;

namespace Orchestra.Backend.Services;

/// <summary>
/// Sistem menü profillerini (Teknisyen, Superuser) tohumlar. Eski global
/// "hiddenMenusForTechnician" ayarını Teknisyen profiline taşıyarak yükseltmede
/// mevcut davranışın bozulmamasını sağlar.
/// </summary>
public static class MenuProfileSeeder
{
    public static async Task SeedAsync(OrchestraDbContext db)
    {
        var added = false;

        if (!await db.MenuProfiles.AnyAsync(p => p.IsSystem && p.Name == "Teknisyen"))
        {
            // Eski global gizli menü listesi → Teknisyen profilinin gizli seti (davranış korunur).
            var hiddenJson = "[]";
            var legacy = await db.AppSettings.FindAsync("hiddenMenusForTechnician");
            if (legacy != null && !string.IsNullOrWhiteSpace(legacy.Value))
                hiddenJson = legacy.Value;

            db.MenuProfiles.Add(new MenuProfile
            {
                Name = "Teknisyen",
                Description = "Standart teknisyen menüleri (atanmamış kullanıcılar için varsayılan)",
                IsSystem = true,
                AllowAllByDefault = true,
                HiddenMenusJson = hiddenJson,
                AllowedMenusJson = "[]"
            });
            added = true;
        }

        if (!await db.MenuProfiles.AnyAsync(p => p.IsSystem && p.Name == "Superuser"))
        {
            db.MenuProfiles.Add(new MenuProfile
            {
                Name = "Superuser",
                Description = "Tüm teknisyen menüleri açık (admin'e özel menüler hariç)",
                IsSystem = true,
                AllowAllByDefault = true,
                HiddenMenusJson = "[]",
                AllowedMenusJson = "[]"
            });
            added = true;
        }

        if (added)
        {
            await db.SaveChangesAsync();
            Console.WriteLine("[SEED] Menu profiles (Teknisyen, Superuser) ensured");
        }
    }
}
