using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Data;
using Orchestra.Backend.Services;
using System.IO.Compression;
using System.Security.Claims;

namespace Orchestra.Backend.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/kasa-log")]
    public class KasaLogController : ControllerBase
    {
        private readonly OrchestraDbContext _db;
        private readonly IEmailService _emailService;
        private readonly ILogger<KasaLogController> _logger;
        private readonly IServiceScopeFactory _scopeFactory;

        public KasaLogController(OrchestraDbContext db, IEmailService emailService, ILogger<KasaLogController> logger, IServiceScopeFactory scopeFactory)
        {
            _db = db;
            _emailService = emailService;
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        [HttpPost("send/{deviceId}")]
        public async Task<IActionResult> SendKasaLogs(string deviceId, CancellationToken ct)
        {
            var username = User.FindFirstValue(ClaimTypes.Name);
            if (string.IsNullOrEmpty(username))
                return Unauthorized(new { error = "Kullanıcı bilgisi bulunamadı" });

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username, ct);
            if (user == null)
                return Unauthorized(new { error = "Kullanıcı bulunamadı" });
            if (string.IsNullOrWhiteSpace(user.Email))
                return BadRequest(new { error = "Kullanıcının e-posta adresi tanımlanmamış. Ayarlar sayfasından e-posta ekleyin." });

            var device = await _db.StoreDevices.AsNoTracking().FirstOrDefaultAsync(d => d.DeviceId == deviceId, ct);
            if (device == null)
                return NotFound(new { error = "Cihaz bulunamadı" });

            var ip = device.CalculatedIpAddress;
            var today = DateTime.Now;
            var uncBase = $@"\\{ip}\C$\GeniusPOS";
            var tempDir = Path.Combine(Path.GetTempPath(), $"mudosoft_kasalog_{Guid.NewGuid():N}");

            try
            {
                Directory.CreateDirectory(tempDir);

                // 1. Dosyaları bul
                var dateYmd = today.ToString("yyyy_MM_dd");
                var dateDmy = today.ToString("ddMMyyyy");

                var filesToCopy = new List<string>();

                foreach (var f in Directory.GetFiles(uncBase, $"IMPDLL_{dateYmd}*.*"))
                    filesToCopy.Add(f);
                foreach (var f in Directory.GetFiles(uncBase, $"geniuspos_*_{dateDmy}.log"))
                    filesToCopy.Add(f);

                if (filesToCopy.Count == 0)
                    return BadRequest(new { error = "Bugüne ait log dosyası bulunamadı." });

                // 2. Temp'e kopyala
                var copiedFiles = new List<string>();
                foreach (var src in filesToCopy)
                {
                    var destFile = Path.Combine(tempDir, Path.GetFileName(src));
                    System.IO.File.Copy(src, destFile);
                    copiedFiles.Add(destFile);
                }

                // 3. Zip yap
                var zipPath = Path.Combine(Path.GetTempPath(), $"kasa_log_{device.StoreCode}_{device.DeviceType}_{today:yyyyMMdd_HHmmss}.zip");
                ZipFile.CreateFromDirectory(tempDir, zipPath, CompressionLevel.Optimal, false);
                var zipBytes = await System.IO.File.ReadAllBytesAsync(zipPath, ct);

                // 4. Mail gönder
                var subject = $"[MudoSoft] Kasa Logları - {device.StoreName} {device.DeviceType} ({today:dd.MM.yyyy})";
                var zipFileName = Path.GetFileName(zipPath);
                var fileNames = copiedFiles.Select(Path.GetFileName).ToList();

                var htmlBody = $@"
<div style='font-family:Arial,sans-serif;max-width:600px;margin:0 auto'>
  <div style='background:#1e1b4b;color:white;padding:16px 24px;border-radius:8px 8px 0 0'>
    <h2 style='margin:0;font-size:18px'>Kasa Log Raporu</h2>
  </div>
  <div style='background:#f8fafc;padding:24px;border:1px solid #e2e8f0;border-top:none;border-radius:0 0 8px 8px'>
    <table style='width:100%;border-collapse:collapse'>
      <tr><td style='padding:8px 0;color:#64748b;width:130px'>Magaza:</td><td style='padding:8px 0;font-weight:600'>{device.StoreName}</td></tr>
      <tr><td style='padding:8px 0;color:#64748b'>Kasa:</td><td style='padding:8px 0;font-weight:600'>{device.DeviceType}</td></tr>
      <tr><td style='padding:8px 0;color:#64748b'>IP Adresi:</td><td style='padding:8px 0;font-family:monospace'>{ip}</td></tr>
      <tr><td style='padding:8px 0;color:#64748b'>Tarih:</td><td style='padding:8px 0'>{today:dd.MM.yyyy HH:mm}</td></tr>
      <tr><td style='padding:8px 0;color:#64748b'>Talep Eden:</td><td style='padding:8px 0'>{user.FullName} ({username})</td></tr>
    </table>
    <hr style='border:none;border-top:1px solid #e2e8f0;margin:16px 0'/>
    <p style='color:#64748b;font-size:13px'><strong>{fileNames.Count} dosya:</strong></p>
    <ul style='color:#334155;font-size:13px;font-family:monospace'>
      {string.Join("\n", fileNames.Select(f => $"<li>{f}</li>"))}
    </ul>
  </div>
</div>";

                // Office 365 banner ~105s sürdüğünden (IP reputation lookup) fire-and-forget gönder.
                // Kullanıcı hemen yanıt alır; email arka planda iletilir.
                var capturedEmail     = user.Email;
                var capturedSubject   = subject;
                var capturedBody      = htmlBody;
                var capturedBytes     = zipBytes;
                var capturedFileName  = zipFileName;
                var capturedDeviceId  = deviceId;
                var capturedFileCount = fileNames.Count;

                _ = Task.Run(async () =>
                {
                    using var scope = _scopeFactory.CreateScope();
                    var emailSvc = scope.ServiceProvider.GetRequiredService<IEmailService>();
                    var sent = await emailSvc.SendEmailWithAttachmentAsync(capturedEmail, capturedSubject, capturedBody, capturedBytes, capturedFileName);
                    if (sent)
                        _logger.LogInformation("Kasa logları gönderildi (bg): {DeviceId} -> {Email} ({Count} dosya)", capturedDeviceId, capturedEmail, capturedFileCount);
                    else
                        _logger.LogError("Kasa logları gönderilemedi (bg): {DeviceId} -> {Email}", capturedDeviceId, capturedEmail);
                });

                return Ok(new
                {
                    success = true,
                    message = $"{fileNames.Count} log dosyası {user.Email} adresine gönderiliyor.",
                    fileCount = fileNames.Count
                });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Kasa log gönderiminde hata: {DeviceId}", deviceId);
                return StatusCode(500, new { error = $"Hata: {ex.Message}" });
            }
            finally
            {
                // Temizlik
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
                try
                {
                    var zipClean = Path.Combine(Path.GetTempPath(), $"kasa_log_{device.StoreCode}_{device.DeviceType}_{today:yyyyMMdd_HHmmss}.zip");
                    if (System.IO.File.Exists(zipClean)) System.IO.File.Delete(zipClean);
                }
                catch { }
            }
        }
    }
}
