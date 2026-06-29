using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Data;
using Orchestra.Backend.Services;
using System.Security.Claims;
using System.Text;

namespace Orchestra.Backend.Controllers;

// ── Request / Response DTO'ları ────────────────────────────────────────────

public class OutageMailRequest
{
    /// <summary>Mağaza kodları (birden fazla olabilir)</summary>
    public List<int> StoreCodes { get; set; } = new();

    /// <summary>Sorun türü anahtarı (OutageMailTemplates içindeki key)</summary>
    public string IssueKey { get; set; } = "internet-outage-normal";

    /// <summary>Deprecated. Live mails always start with "Merhaba Zafer Bey".</summary>
    public string? Greeting { get; set; }

    /// <summary>Opsiyonel ek not (paragraf sonuna eklenir)</summary>
    public string? AdditionalNotes { get; set; }

    /// <summary>Sürekli kopma / uzun süreli için gün sayısı (ör. "yaklaşık 15 gündür")</summary>
    public int? Days { get; set; }

    /// <summary>Deprecated. Live recipient is fixed server-side.</summary>
    public string? RecipientOverride { get; set; }

    /// <summary>Kullanıcı önizlemeyi manuel düzenlediyse bu alan dolu gelir. Doluysa şablon yerine bu metin gönderilir.</summary>
    public string? EditedPlainText { get; set; }

    /// <summary>Kullanıcı Kime alanını düzenlediyse bu alan dolu gelir.</summary>
    public string? ToOverride { get; set; }

    /// <summary>Kullanıcı CC listesini düzenlediyse bu alan dolu gelir.</summary>
    public List<string>? CcOverride { get; set; }
}

public class OutageMailTemplate
{
    public string Key { get; set; } = "";
    public string Category { get; set; } = "";     // Internet / POS
    public string Label { get; set; } = "";
    public string BodyTemplate { get; set; } = ""; // {stores}, {days}
    public string SubjectSuffix { get; set; } = ""; // "İnternet Kesintisi Hk." gibi
    public bool NeedsDays { get; set; } = false;
}

public class OutageMailPreviewDto
{
    public string Subject { get; set; } = "";
    public string PlainText { get; set; } = "";
    public string HtmlBody { get; set; } = "";
    public string To { get; set; } = "";
    public List<string> Cc { get; set; } = new();
    public List<StoreBlock> Stores { get; set; } = new();
}

public class StoreBlock
{
    public int StoreCode { get; set; }
    /// <summary>StoreDevices tablosundaki dahili mağaza kodu (router IP ile eşleşen)</summary>
    public int? InternalStoreCode { get; set; }
    public string StoreName { get; set; } = "";
    public string RouterIp { get; set; } = "";
    public string? Address { get; set; }
    public string? ManagerName { get; set; }
    public string? ManagerPhone { get; set; }
}

public class OutageMailSendResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string To { get; set; } = "";
    public List<string> Cc { get; set; } = new();
}

// ── Şablon kataloğu ────────────────────────────────────────────────────────

public static class OutageMailTemplates
{
    // Yeni iskelet (orta detay):
    //   {salutation}
    //   {problemSentence}        — sorunun ne olduğu (mağaza + adres cümle içine girer)
    //   {impactSentence}         — operasyonel etki (kasa/satış/iletişim)
    //   {notesBlock}             — kullanıcının ek notu (opsiyonel)
    //   {storesBlock}            — her mağaza için ad, adres, IP, iletişim
    //   {requestSentence}        — beklenen aksiyon
    //   {signoff}
    public static readonly List<OutageMailTemplate> All = new()
    {
        // ── İnternet ─────────────────────────────────────────────────────
        new() {
            Key = "internet-kesinti", Category = "İnternet", Label = "İnternet Kesintisi",
            SubjectSuffix = "İnternet Kesintisi Hk.",
            BodyTemplate =
                "{salutation},\n\n" +
                "{storesSentence} mağaza{mizOrLariMiz}da internet erişimi tamamen kesilmiştir.{notesBlock}\n\n" +
                "{storesBlock}" +
                "Acil olarak kontrol eder misiniz?\n\nİyi çalışmalar.",
        },
        new() {
            Key = "internet-yavaslik", Category = "İnternet", Label = "Yavaşlık",
            SubjectSuffix = "İnternet Yavaşlığı Hk.",
            BodyTemplate =
                "{salutation},\n\n" +
                "{storesSentence} mağaza{mizOrLariMiz}da internet hızında ciddi düşüş / saturasyon yaşanıyor.{notesBlock}\n\n" +
                "{storesBlock}" +
                "Mağazanın acil hattını kontrol edip, 30 dk'lık ip accounting çıktısı iletebilir misiniz?\n\nİyi çalışmalar.",
        },
        new() {
            Key = "internet-kopma", Category = "İnternet", Label = "Sürekli Kopma",
            SubjectSuffix = "Sürekli Hat Kopması Hk.",
            BodyTemplate =
                "{salutation},\n\n" +
                "{storesSentence} mağaza{mizOrLariMiz}da internet bağlantısı gün içinde defalarca kesiliyor.{notesBlock}\n\n" +
                "{storesBlock}" +
                "Hattı inceleyebilir misiniz?\n\nİyi çalışmalar.",
        },
        new() {
            Key = "internet-problem", Category = "İnternet", Label = "Bağlantı Problemi",
            SubjectSuffix = "Hat Sorunu Hk.",
            BodyTemplate =
                "{salutation},\n\n" +
                "{storesSentence} mağaza{mizOrLariMiz}da internet bağlantısında sorun var; bağlantı kurulsa da uygulamalar düzgün çalışmıyor.{notesBlock}\n\n" +
                "{storesBlock}" +
                "Mağazanın acil hattını kontrol edip, 30 dk'lık ip accounting çıktısı iletebilir misiniz?\n\nİyi çalışmalar.",
        },

        // ── POS / Kasa ───────────────────────────────────────────────────
        new() {
            Key = "pos-ariza", Category = "POS", Label = "Kasa Arızası",
            SubjectSuffix = "POS Arızası Hk.",
            BodyTemplate =
                "{salutation},\n\n" +
                "{storesSentence} mağaza{mizOrLariMiz}da POS / kasa tarafında arıza yaşanıyor.{notesBlock} Satış işlemleri aksıyor.\n\n" +
                "{storesBlock}" +
                "En kısa sürede destek verebilir misiniz?\n\nİyi çalışmalar.",
        },
        new() {
            Key = "pos-odeme", Category = "POS", Label = "Ödeme Alınamıyor",
            SubjectSuffix = "ACİL · Ödeme Alınamıyor Hk.",
            BodyTemplate =
                "{salutation},\n\n" +
                "{storesSentence} mağaza{mizOrLariMiz}da kasalarda ödeme alınamıyor; kart işlemleri red dönmekte, satış kapatılamıyor.{notesBlock}\n\n" +
                "{storesBlock}" +
                "ACİL müdahale rica ederiz.\n\nİyi çalışmalar.",
        },
    };

    public static OutageMailTemplate? Get(string key) => All.FirstOrDefault(t => t.Key == key);
}

// ── Controller ─────────────────────────────────────────────────────────────

[ApiController]
[Route("api/outage-mail")]
[Authorize]
public class OutageMailController : ControllerBase
{
    private readonly OrchestraDbContext _db;
    private readonly IEmailService _email;
    private readonly ILogger<OutageMailController> _logger;

    // Sabit alıcı. İleride AppSettings'e alınabilir.
    private const string LiveRecipient = "onur.karagoz@turkcell.com.tr";
    private const string FixedCcRecipient = "MudoBTDestek@mudo.com.tr";
    private const string LiveGreeting = "Onur Bey";

    public OutageMailController(OrchestraDbContext db, IEmailService email, ILogger<OutageMailController> logger)
    {
        _db = db;
        _email = email;
        _logger = logger;
    }

    [HttpGet("templates")]
    public ActionResult<object> GetTemplates()
    {
        var grouped = OutageMailTemplates.All
            .GroupBy(t => t.Category)
            .Select(g => new
            {
                category = g.Key,
                items = g.Select(t => new { t.Key, t.Label, t.NeedsDays }).ToList()
            });
        return Ok(grouped);
    }

    [HttpPost("preview")]
    public async Task<ActionResult<OutageMailPreviewDto>> Preview([FromBody] OutageMailRequest req)
    {
        var preview = await BuildPreviewAsync(req);
        if (preview == null) return BadRequest(new { error = "Geçersiz şablon veya mağaza" });
        return Ok(preview);
    }

    [HttpPost("send")]
    public async Task<ActionResult<OutageMailSendResult>> Send([FromBody] OutageMailRequest req)
    {
        var preview = await BuildPreviewAsync(req);
        if (preview == null) return BadRequest(new OutageMailSendResult { Success = false, Error = "Geçersiz şablon veya mağaza" });

        // Kullanıcı önizlemeyi düzenlediyse onun metnini kullan
        var plainText = !string.IsNullOrWhiteSpace(req.EditedPlainText) ? req.EditedPlainText! : preview.PlainText;
        var escaped = System.Net.WebUtility.HtmlEncode(plainText).Replace("\n", "<br/>");
        var htmlBody = $@"<div style=""font-family: Calibri, Arial, sans-serif; font-size: 14px; color: #1f2937; line-height: 1.55;"">{escaped}</div>";

        // Kullanıcı Kime/CC alanını düzenlediyse override kullan
        var toAddress = !string.IsNullOrWhiteSpace(req.ToOverride) ? req.ToOverride!.Trim() : preview.To;
        var ccList = req.CcOverride != null
            ? req.CcOverride.Select(e => e.Trim()).Where(e => e.Length > 0).ToList()
            : preview.Cc;

        var (ok, err) = await _email.SendWithCcAsync(
            toAddress,
            ccList,
            preview.Subject,
            htmlBody,
            plainText);

        return Ok(new OutageMailSendResult
        {
            Success = ok,
            Error = ok ? null : err,
            To = toAddress,
            Cc = ccList,
        });
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private async Task<OutageMailPreviewDto?> BuildPreviewAsync(OutageMailRequest req)
    {
        var template = OutageMailTemplates.Get(req.IssueKey);
        if (template == null || req.StoreCodes == null || req.StoreCodes.Count == 0)
            return null;

        // Mağaza blokları (StoreManagers + StoreDevices router IP eşleştirmesi)
        var storeCodes = req.StoreCodes.Distinct().ToList();

        var managers = await _db.StoreManagers
            .Where(m => storeCodes.Contains(m.StoreCode))
            .ToListAsync();

        // Mağaza adlarını topla (StoreManagers'dan)
        var storeNames = managers
            .Select(m => m.StoreName.Trim())
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct()
            .ToList();

        // Router IP'lerini mağaza adına göre StoreDevices'den al
        // (StoreManagers ve StoreDevices farklı kod sistemleri kullanıyor)
        var routersByName = await _db.StoreDevices
            .Where(d => d.DeviceType == "ROUTER" && storeNames.Contains(d.StoreName))
            .Select(d => new { d.StoreCode, d.StoreName, d.CalculatedIpAddress })
            .ToListAsync();

        var blocks = storeCodes
            .Select(code =>
            {
                var mgr = managers.FirstOrDefault(m => m.StoreCode == code);
                var storeName = mgr?.StoreName?.Trim() ?? $"Mağaza {code}";
                var router = routersByName.FirstOrDefault(r => r.StoreName == storeName);
                return new StoreBlock
                {
                    StoreCode = code,
                    InternalStoreCode = router?.StoreCode,
                    StoreName = storeName,
                    RouterIp = !string.IsNullOrWhiteSpace(router?.CalculatedIpAddress)
                        ? router!.CalculatedIpAddress
                        : "",
                    Address = string.IsNullOrWhiteSpace(mgr?.Address) ? null : mgr!.Address,
                    ManagerName = string.IsNullOrWhiteSpace(mgr?.FullName) ? null : mgr!.FullName,
                    ManagerPhone = string.IsNullOrWhiteSpace(mgr?.Phone) ? null : mgr!.Phone,
                };
            })
            .ToList();

        // CC: oturum açan kullanıcının email'i
        var cc = new List<string>();
        AddCc(cc, FixedCcRecipient);
        var username = User.FindFirstValue(ClaimTypes.Name);
        if (!string.IsNullOrWhiteSpace(username))
        {
            var email = await _db.Users
                .Where(u => u.Username == username)
                .Select(u => u.Email)
                .FirstOrDefaultAsync();
            AddCc(cc, email);
        }

        var plainText = BuildPlainText(template, blocks, req);
        var htmlBody = BuildHtml(template, blocks, req, plainText);
        var subject = BuildSubject(template, blocks);

        return new OutageMailPreviewDto
        {
            Subject = subject,
            PlainText = plainText,
            HtmlBody = htmlBody,
            To = LiveRecipient,
            Cc = cc,
            Stores = blocks,
        };
    }

    private static void AddCc(List<string> cc, string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return;

        var normalized = email.Trim();
        if (string.Equals(normalized, LiveRecipient, StringComparison.OrdinalIgnoreCase)) return;
        if (cc.Any(existing => string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase))) return;

        cc.Add(normalized);
    }

    private static string BuildSubject(OutageMailTemplate tpl, List<StoreBlock> blocks)
    {
        // Format: "147 Balıkesir Burda Giyim İnternet Kesintisi Hk."
        // Birden fazla mağaza: "147 Balıkesir Burda Giyim, 152 Buyaka City İnternet Kesintisi Hk."
        var storesPart = string.Join(", ", blocks.Select(b =>
        {
            var code = b.InternalStoreCode ?? b.StoreCode;
            return $"{code} {b.StoreName}";
        }));
        return $"{storesPart} {tpl.SubjectSuffix}";
    }

    private static string BuildPlainText(OutageMailTemplate tpl, List<StoreBlock> blocks, OutageMailRequest req)
    {
        var salutation = $"Merhaba {LiveGreeting}";

        // Mağaza cümlesi
        string storesSentence;
        string mizOrLariMiz; // "mızda" vs "larımızda" — ek olarak eklenen hecenin tamamı değil sadece "lar" kısmı
        if (blocks.Count == 1)
        {
            storesSentence = blocks[0].StoreName;
            mizOrLariMiz = "mız";   // "mağazamızda"
        }
        else
        {
            var names = blocks.Select(b => b.StoreName).ToList();
            storesSentence = names.Count == 2
                ? $"{names[0]} ve {names[1]}"
                : string.Join(", ", names.Take(names.Count - 1)) + " ve " + names[^1];
            mizOrLariMiz = "larımız"; // "mağazalarımızda"
        }

        // Mağaza bilgileri bloğu — her mağaza için ad, IP, iletişim (kompakt format)
        var storesSb = new StringBuilder();
        for (var i = 0; i < blocks.Count; i++)
        {
            var b = blocks[i];
            var code = b.InternalStoreCode ?? b.StoreCode;
            storesSb.Append("• ").Append(code).Append(' ').Append(b.StoreName).AppendLine();
            if (!string.IsNullOrWhiteSpace(b.RouterIp))
                storesSb.Append("  IP: ").Append(b.RouterIp).AppendLine();
            if (!string.IsNullOrWhiteSpace(b.ManagerName) || !string.IsNullOrWhiteSpace(b.ManagerPhone))
            {
                storesSb.Append("  İletişim: ");
                if (!string.IsNullOrWhiteSpace(b.ManagerName)) storesSb.Append(b.ManagerName);
                if (!string.IsNullOrWhiteSpace(b.ManagerName) && !string.IsNullOrWhiteSpace(b.ManagerPhone)) storesSb.Append(" - ");
                if (!string.IsNullOrWhiteSpace(b.ManagerPhone)) storesSb.Append(b.ManagerPhone);
                storesSb.AppendLine();
            }
            storesSb.AppendLine();
        }

        // Ek not: sorun cümlesinin sonuna eklenir, boşsa yok sayılır
        var notesBlock = string.IsNullOrWhiteSpace(req.AdditionalNotes)
            ? ""
            : " " + req.AdditionalNotes!.Trim();

        return tpl.BodyTemplate
            .Replace("{salutation}", salutation)
            .Replace("{storesSentence}", storesSentence)
            .Replace("{mizOrLariMiz}", mizOrLariMiz)
            .Replace("{storesBlock}", storesSb.ToString())
            .Replace("{notesBlock}", notesBlock)
            .Replace("{days}", (req.Days ?? 0).ToString());
    }

    private static string BuildHtml(OutageMailTemplate tpl, List<StoreBlock> blocks, OutageMailRequest req, string plainText)
    {
        // Kurumsal mail istemcilerinde güvenli kalması için sade HTML. Esas içerik plain-text
        // ruhunda — sadece font + router tablosu vurgulanıyor.
        var escaped = System.Net.WebUtility.HtmlEncode(plainText).Replace("\n", "<br/>");
        return $@"<div style=""font-family: Calibri, Arial, sans-serif; font-size: 14px; color: #1f2937; line-height: 1.55;"">
{escaped}
</div>";
    }
}
