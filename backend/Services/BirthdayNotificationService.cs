using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Data;

namespace Orchestra.Backend.Services;

// personel.json içindeki her satırın karşılığı
internal sealed class PersonelRecord
{
    [JsonPropertyName("no")]   public string No { get; set; } = "";
    [JsonPropertyName("ad")]   public string Ad { get; set; } = "";
    [JsonPropertyName("soyad")] public string Soyad { get; set; } = "";
    [JsonPropertyName("gorev")] public string Gorev { get; set; } = "";
    [JsonPropertyName("dogumTarih")] public string DogumTarih { get; set; } = "";
}

public class BirthdayNotificationService : BackgroundService
{
    // btTeam.ts ile senkron tutulmalı
    private static readonly (string No, string? AdOverride, string? SoyadOverride)[] TeamConfigs =
    [
        ("P20516", null,        null),
        ("P35448", null,        null),
        ("P36914", null,        null),
        ("P37859", "NİSAN",     "GÜRBÜZ"),
        ("P34065", null,        null),
        ("P34076", null,        null),
        ("P36941", "MUSTAFA",   "KOCATEPE"),
        ("P38540", null,        null),
        ("P34767", "ÜMMÜHAN",   "KURT"),
        ("P35690", "ELİF",      "KARAKAŞ"),
        ("P37112", "KADİR",     "TURAN"),
        ("P13947", "ÜMİT",      "SARILI"),
        ("P32993", null,        null),
        ("P9632",  null,        null),
        ("P38544", "UĞUR",      "HAMAMCI"),
        ("P25763", null,        null),
        ("P33672", null,        null),
        ("P36311", null,        null),
        ("P18265", null,        null),
        ("P34642", null,        null),
        ("P38215", null,        null),
    ];

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BirthdayNotificationService> _logger;
    private readonly IWebHostEnvironment _env;
    private readonly ILdapDirectoryService _ldap;

    public BirthdayNotificationService(
        IServiceProvider serviceProvider,
        ILogger<BirthdayNotificationService> logger,
        IWebHostEnvironment env,
        ILdapDirectoryService ldap)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _env = env;
        _ldap = ldap;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🎂 BirthdayNotificationService başladı.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.Now;
            var nextMidnight = now.Date.AddDays(1);
            var delay = nextMidnight - now;

            _logger.LogInformation("🎂 Bir sonraki doğum günü kontrolü: {Next:dd.MM.yyyy HH:mm}", nextMidnight);

            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { break; }

            if (!stoppingToken.IsCancellationRequested)
                await CheckAndSendAsync(stoppingToken);
        }
    }

    private async Task CheckAndSendAsync(CancellationToken stoppingToken)
    {
        try
        {
            var personelMap = LoadPersonelJson();
            if (personelMap == null)
            {
                _logger.LogWarning("🎂 personel.json okunamadı, doğum günü kontrolü atlandı.");
                return;
            }

            var today = DateTime.Now;

            // Bugün doğum günü olan ekip üyelerini bul
            var birthdayMembers = new List<(string FullName, int AgeTurning)>();
            foreach (var (no, adOverride, soyadOverride) in TeamConfigs)
            {
                if (!personelMap.TryGetValue(no, out var rec)) continue;

                if (!TryParseDate(rec.DogumTarih, out var birthDate)) continue;
                if (birthDate.Month != today.Month || birthDate.Day != today.Day) continue;

                var ad    = adOverride    ?? rec.Ad;
                var soyad = soyadOverride ?? rec.Soyad;
                var fullName = TurkishTitleCase($"{ad} {soyad}").Trim();
                var age = today.Year - birthDate.Year;

                birthdayMembers.Add((fullName, age));
            }

            if (birthdayMembers.Count == 0)
            {
                _logger.LogInformation("🎂 Bugün ({Date:dd.MM.yyyy}) doğum günü olan ekip üyesi yok.", today);
                return;
            }

            _logger.LogInformation("🎂 {Count} kişinin doğum günü var, eşleştirme yapılıyor...", birthdayMembers.Count);

            using var scope = _serviceProvider.CreateScope();
            var db           = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();
            var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

            var usersWithEmail = await db.Users
                .Where(u => u.IsActive && !string.IsNullOrEmpty(u.Email))
                .Select(u => new { u.FullName, u.Email })
                .ToListAsync(stoppingToken);

            foreach (var (fullName, age) in birthdayMembers)
            {
                var email = await ResolveEmailAsync(fullName, usersWithEmail.Select(u => (u.FullName, u.Email!)).ToList(), stoppingToken);

                if (email == null)
                {
                    _logger.LogWarning("🎂 {Name} için e-posta adresi bulunamadı (Orchestra + LDAP), mail atlandı.", fullName);
                    continue;
                }

                var subject = $"🎂 Mutlu Yıllar, {fullName}!";
                var html    = BuildBirthdayHtml(fullName, age);
                var result  = await emailService.SendAlarmEmailAsync([email], subject, html);

                if (result.AllSucceeded)
                    _logger.LogInformation("🎉 Doğum günü maili gönderildi: {Name} <{Email}>", fullName, email);
                else
                    _logger.LogWarning("⚠️ Doğum günü maili gönderilemedi: {Name} — {Error}",
                        fullName, result.FailedRecipients.FirstOrDefault().Error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Doğum günü bildirimi sırasında hata oluştu.");
        }
    }

    private Dictionary<string, PersonelRecord>? LoadPersonelJson()
    {
        // Dev: repo/backend/../frontend/src/data/personel.json
        // Prod: C:\projects\orchestra\backend\..\repo\frontend\src\data\personel.json
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(_env.ContentRootPath, "..", "frontend", "src", "data", "personel.json")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "repo", "frontend", "src", "data", "personel.json")),
        };

        foreach (var path in candidates)
        {
            if (!File.Exists(path)) continue;
            try
            {
                var json    = File.ReadAllText(path);
                var records = JsonSerializer.Deserialize<List<PersonelRecord>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (records == null) continue;

                _logger.LogInformation("🎂 personel.json yüklendi: {Path} ({Count} kayıt)", path, records.Count);
                return records.ToDictionary(r => r.No, r => r);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "personel.json okunamadı: {Path}", path);
            }
        }

        return null;
    }

    private async Task<string?> ResolveEmailAsync(
        string fullName,
        List<(string FullName, string Email)> dbUsers,
        CancellationToken ct)
    {
        // 1) Orchestra Users tablosu
        var dbMatch = dbUsers.FirstOrDefault(u =>
            string.Equals(NormalizeForCompare(u.FullName), NormalizeForCompare(fullName),
                          StringComparison.OrdinalIgnoreCase));
        if (dbMatch != default)
        {
            _logger.LogDebug("🎂 {Name} → Orchestra DB'den mail: {Email}", fullName, dbMatch.Email);
            return dbMatch.Email;
        }

        // 2) Active Directory (LDAP)
        if (!_ldap.IsAvailable)
        {
            _logger.LogDebug("🎂 LDAP mevcut değil, {Name} için AD araması atlandı.", fullName);
            return null;
        }

        try
        {
            var results = await _ldap.SearchUsersAsync(fullName, 20, ct);
            var ldapMatch = results.FirstOrDefault(u =>
                u.Enabled &&
                !string.IsNullOrEmpty(u.Email) &&
                string.Equals(NormalizeForCompare(u.DisplayName ?? ""), NormalizeForCompare(fullName),
                              StringComparison.OrdinalIgnoreCase));

            if (ldapMatch != null)
            {
                _logger.LogInformation("🎂 {Name} → LDAP'tan mail bulundu: {Email}", fullName, ldapMatch.Email);
                return ldapMatch.Email;
            }

            _logger.LogDebug("🎂 LDAP araması sonuçsuz: {Name}", fullName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "🎂 LDAP e-posta araması başarısız: {Name}", fullName);
        }

        return null;
    }

    private static bool TryParseDate(string value, out DateTime date)
    {
        date = default;
        if (string.IsNullOrEmpty(value)) return false;
        var parts = value.Split('/');
        if (parts.Length != 3) return false;
        if (!int.TryParse(parts[0], out var d) ||
            !int.TryParse(parts[1], out var m) ||
            !int.TryParse(parts[2], out var y)) return false;
        date = new DateTime(y, m, d);
        return true;
    }

    // Türkçe büyük harf -> küçük harf title case (İ→i, Ş→ş vb.)
    private static string TurkishTitleCase(string value)
    {
        var culture = new System.Globalization.CultureInfo("tr-TR");
        return string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Length == 0 ? w
                : char.ToUpper(w[0], culture) + w[1..].ToLower(culture)));
    }

    // İsim karşılaştırması için Türkçe büyük harfe çevir
    private static string NormalizeForCompare(string value) =>
        value.Trim().ToUpper(new System.Globalization.CultureInfo("tr-TR"));

    internal static string BuildBirthdayHtml(string fullName, int age)
    {
        var safeFullName = System.Net.WebUtility.HtmlEncode(fullName);

        return $@"<!DOCTYPE html>
<html lang=""tr"">
<head>
  <meta charset=""UTF-8"">
  <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
</head>
<body style=""margin:0;padding:0;background:#16132a;font-family:'Segoe UI',Arial,sans-serif;"">
  <table width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background:#16132a;padding:32px 20px;"">
    <tr><td align=""center"">
      <table width=""560"" cellpadding=""0"" cellspacing=""0"" style=""max-width:560px;width:100%;background:#1e1b30;border-radius:16px;"">

        <tr>
          <td style=""background:#241f3a;border-radius:16px 16px 0 0;padding:18px 32px;text-align:center;border-bottom:1px solid #2d2850;"">
            <span style=""color:#a78bfa;font-size:11px;text-transform:uppercase;letter-spacing:4px;font-weight:400;"">MUDO B&#304;LG&#304; TEKNOLOJ&#304;LER&#304;</span>
          </td>
        </tr>

        <tr>
          <td style=""padding:44px 32px 12px;text-align:center;"">
            <span style=""color:#6d5fa0;font-size:13px;vertical-align:top;line-height:2.5;"">&#10022; &middot;</span>
            <span style=""font-size:72px;line-height:1;margin:0 10px;"">&#127874;</span>
            <span style=""color:#6d5fa0;font-size:13px;vertical-align:top;line-height:2.5;"">&middot; &#10022;</span>
          </td>
        </tr>

        <tr>
          <td style=""padding:16px 32px 8px;text-align:center;"">
            <h1 style=""margin:0;color:#ffffff;font-size:34px;font-weight:700;line-height:1.25;font-family:Georgia,'Times New Roman',serif;"">
              Do&#287;um G&#252;n&#252;n&#252;z<br>Kutlu Olsun!
            </h1>
          </td>
        </tr>

        <tr>
          <td style=""padding:20px 48px 16px;"">
            <table width=""100%"" cellpadding=""0"" cellspacing=""0""><tr>
              <td style=""border-top:1px solid #3d3560;""></td>
              <td style=""width:24px;padding:0 6px;text-align:center;""><span style=""color:#7c5cbf;font-size:9px;"">&#9670;</span></td>
              <td style=""border-top:1px solid #3d3560;""></td>
            </tr></table>
          </td>
        </tr>

        <tr>
          <td style=""padding:0 32px 28px;text-align:center;"">
            <p style=""margin:0;color:#a78bfa;font-size:20px;font-weight:400;"">Say&#305;n {safeFullName}</p>
          </td>
        </tr>

        <tr>
          <td style=""padding:0 52px 36px;text-align:center;"">
            <p style=""margin:0 0 16px;color:#c4bdd8;font-size:15px;line-height:1.8;"">
              Yeni ya&#351;&#305;n&#305;z&#305; en i&#231;ten dileklerimizle kutlar&#305;z.
            </p>
            <p style=""margin:0 0 16px;color:#c4bdd8;font-size:15px;line-height:1.8;"">
              MUDO Bilgi Teknolojileri Ekibi olarak, yeni ya&#351;&#305;n&#305;z&#305;n sa&#287;l&#305;k,
              mutluluk ve ba&#351;ar&#305; getirmesini diler; sevdiklerinizle birlikte huzurlu,
              keyifli ve g&#252;zel an&#305;larla dolu bir y&#305;l ge&#231;irmenizi temenni ederiz.
            </p>
            <p style=""margin:0;color:#c4bdd8;font-size:15px;line-height:1.8;"">
              Nice mutlu y&#305;llara.
            </p>
          </td>
        </tr>

        <tr>
          <td style=""padding:0 48px 20px;"">
            <table width=""100%"" cellpadding=""0"" cellspacing=""0""><tr>
              <td style=""border-top:1px solid #2d2850;""></td>
              <td style=""width:24px;padding:0 6px;text-align:center;""><span style=""color:#7c5cbf;font-size:9px;"">&#9670;</span></td>
              <td style=""border-top:1px solid #2d2850;""></td>
            </tr></table>
          </td>
        </tr>

        <tr>
          <td style=""padding:0 32px 36px;text-align:center;border-radius:0 0 16px 16px;"">
            <p style=""margin:0 0 6px;color:#a78bfa;font-size:14px;"">MUDO Bilgi Teknolojileri Ekibi</p>
            <p style=""margin:0 0 10px;color:#ffffff;font-size:15px;font-weight:700;"">Orchestra</p>
            <p style=""margin:0;color:#4a4466;font-size:12px;"">Bu mesaj Orchestra taraf&#305;ndan otomatik olarak g&#246;nderilmi&#351;tir.</p>
          </td>
        </tr>

      </table>
    </td></tr>
  </table>
</body>
</html>";
    }
}
