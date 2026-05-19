using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Data;
using Orchestra.Backend.Models;

namespace Orchestra.Backend.Services;

/// <summary>
/// Seeds the default "Standart Mağaza Açılışı" checklist template if no default exists.
/// Mirrors the hardware sheet: Donanım (Ümit Sanlı), Sistem (İsa Eraslan),
/// Router (Gökhan Baltacı), Yazarkasa (Tuğrul Dönmez).
/// </summary>
public static class StoreOpeningTemplateSeeder
{
    // Roller — UI'da da bu sabit isimler kullanılır.
    public const string RoleDonanim = "Donanım Sorumlusu";
    public const string RoleSistem = "Sistem / Network Sorumlusu";
    public const string RoleRouter = "Router Sorumlusu";
    public const string RoleKasa = "Kasa Sorumlusu";

    public static async Task SeedAsync(OrchestraDbContext db)
    {
        if (await db.StoreOpeningTemplates.AnyAsync()) return;

        var t = new StoreOpeningTemplate
        {
            Name = "Standart Mağaza Açılışı",
            Description = "Tüm donanım kategorilerini kapsayan varsayılan açılış şablonu",
            IsDefault = true,
            CreatedBy = "system",
            CreatedAt = DateTime.UtcNow
        };

        var items = new List<StoreOpeningTemplateItem>();
        int order = 0;

        void Add(string category, string role, string name, string? parent = null, bool serial = false, bool asset = false)
        {
            items.Add(new StoreOpeningTemplateItem
            {
                CategoryName = category,
                AssignedRole = role,
                ItemName = name,
                ParentName = parent,
                HasSerialNumber = serial,
                HasAssetNumber = asset,
                SortOrder = order
            });
            order += 10;
        }

        // ==== DONANIM (Ümit Sanlı) ====
        Add("Monitör", RoleDonanim, "Monitör", asset: true);
        Add("Monitör", RoleDonanim, "Güç Kablosu", "Monitör");
        Add("Monitör", RoleDonanim, "Görüntü Kablosu (HDMI / VGA)", "Monitör");

        Add("El Terminali (Zebra)", RoleDonanim, "El Terminali", asset: true, serial: true);
        Add("El Terminali (Zebra)", RoleDonanim, "USB Kablo", "El Terminali");
        Add("El Terminali (Zebra)", RoleDonanim, "Adaptör", "El Terminali");

        Add("Etiket Yazıcı (Zebra)", RoleDonanim, "Etiket Yazıcı", asset: true, serial: true);
        Add("Etiket Yazıcı (Zebra)", RoleDonanim, "Adaptör", "Etiket Yazıcı");
        Add("Etiket Yazıcı (Zebra)", RoleDonanim, "USB Kablo", "Etiket Yazıcı");

        Add("Kişi Sayma Cihazı", RoleDonanim, "Kişi Sayma Cihazı", asset: true);
        Add("Kişi Sayma Cihazı", RoleDonanim, "Adaptör", "Kişi Sayma Cihazı");

        Add("UPS (Güç Kaynağı)", RoleDonanim, "UPS", asset: true, serial: true);
        Add("UPS (Güç Kaynağı)", RoleDonanim, "Güç Kablosu", "UPS");

        // ==== SİSTEM / NETWORK (İsa Eraslan) ====
        Add("Access Point", RoleSistem, "Access Point", asset: true);
        Add("Access Point", RoleSistem, "AP Güç Kablosu", "Access Point");

        Add("PC", RoleSistem, "PC", asset: true, serial: true);
        Add("PC", RoleSistem, "Güç Kablosu", "PC");
        Add("PC", RoleSistem, "Mouse", "PC");
        Add("PC", RoleSistem, "Klavye", "PC");

        Add("Laptop", RoleSistem, "Laptop", asset: true, serial: true);
        Add("Laptop", RoleSistem, "Mouse", "Laptop");
        Add("Laptop", RoleSistem, "Güç Adaptörü", "Laptop");

        Add("IP Phone", RoleSistem, "IP Phone", asset: true);
        Add("IP Phone", RoleSistem, "Güç Kablosu", "IP Phone");

        Add("Kamera", RoleSistem, "Kamera", asset: true);

        Add("NVR (Kayıt Cihazı)", RoleSistem, "NVR", asset: true, serial: true);
        Add("NVR (Kayıt Cihazı)", RoleSistem, "Güç Kablosu", "NVR");

        Add("Switch", RoleSistem, "Switch", asset: true);
        Add("Switch", RoleSistem, "Güç Kablosu", "Switch");

        // ==== ROUTER (Gökhan Baltacı) ====
        Add("Router", RoleRouter, "Router", asset: true, serial: true);
        Add("Router", RoleRouter, "Güç Kablosu", "Router");

        // ==== YAZARKASA (Tuğrul Dönmez) ====
        Add("Yazarkasa", RoleKasa, "Base", asset: true, serial: true);
        Add("Yazarkasa", RoleKasa, "Güç Kablosu (Base)", "Base");
        Add("Yazarkasa", RoleKasa, "Ethernet Kablosu (Base)", "Base");

        Add("Yazarkasa", RoleKasa, "Printer", asset: true, serial: true);
        Add("Yazarkasa", RoleKasa, "Kırmızı Güç Kablosu (Printer)", "Printer");
        Add("Yazarkasa", RoleKasa, "Ethernet Kablosu (Printer)", "Printer");

        Add("Yazarkasa", RoleKasa, "Çekmece", asset: true);
        Add("Yazarkasa", RoleKasa, "Çekmece Kablosu", "Çekmece");

        Add("Yazarkasa", RoleKasa, "Müşteri Ekranı", asset: true);
        Add("Yazarkasa", RoleKasa, "DP Kablosu (Müşteri Ekranı)", "Müşteri Ekranı");
        Add("Yazarkasa", RoleKasa, "Yeşil Güç Kablosu (Müşteri Ekranı)", "Müşteri Ekranı");
        Add("Yazarkasa", RoleKasa, "Müşteri Ekranı Ayağı", "Müşteri Ekranı");

        Add("Yazarkasa", RoleKasa, "Scanner", asset: true);
        Add("Yazarkasa", RoleKasa, "Fiyat Ekranı (Display)", asset: true);
        Add("Yazarkasa", RoleKasa, "Ethernet Kablosu (POS Cihazı)");

        t.Items = items;
        db.StoreOpeningTemplates.Add(t);
        await db.SaveChangesAsync();
    }
}
