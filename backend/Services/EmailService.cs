using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MailKit.Net.Smtp;
using MimeKit;
using MudoSoft.Backend.Data;
using Microsoft.EntityFrameworkCore;

namespace MudoSoft.Backend.Services;

public class EmailSendResult
{
    public bool AllSucceeded => FailedRecipients.Count == 0 && SucceededRecipients.Count > 0;
    public bool PartialSuccess => SucceededRecipients.Count > 0 && FailedRecipients.Count > 0;
    public List<string> SucceededRecipients { get; set; } = new();
    public List<(string Recipient, string Error)> FailedRecipients { get; set; } = new();
}

public interface IEmailService
{
    Task<EmailSendResult> SendAlarmEmailAsync(List<string> recipients, string subject, string htmlBody);
    Task<bool> SendEmailWithAttachmentAsync(string recipient, string subject, string htmlBody, byte[] attachment, string attachmentName);
    Task<(bool success, string error)> TestConnectionAsync();
    Task<(bool success, string message)> SendTestEmailAsync(string recipient, string requestedBy);
    Task<(bool success, string error)> SendWithCcAsync(string to, IEnumerable<string> cc, string subject, string htmlBody, string plainTextBody);
}

public class EmailService : IEmailService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmailService> _logger;
    private readonly string _encryptionKey;

    public EmailService(IServiceScopeFactory scopeFactory, ILogger<EmailService> logger, IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        // JWT key'den SMTP sifre sifrelemesi icin anahtar turet
        var jwtKey = Environment.GetEnvironmentVariable("JWT_SECRET_KEY")
            ?? configuration["Jwt:Key"]
            ?? "MudoSoftDefaultKey2026";
        _encryptionKey = jwtKey;
    }

    public async Task<EmailSendResult> SendAlarmEmailAsync(List<string> recipients, string subject, string htmlBody)
    {
        var result = new EmailSendResult();
        var config = await GetSmtpConfigAsync();
        if (config == null)
        {
            _logger.LogWarning("SMTP yapilandirmasi bulunamadi, e-posta gonderilemedi");
            foreach (var r in recipients)
                result.FailedRecipients.Add((r, "SMTP yapılandırması bulunamadı"));
            return result;
        }

        foreach (var recipient in recipients)
        {
            try
            {
                await SendSingleEmailAsync(config, recipient, subject, htmlBody);
                result.SucceededRecipients.Add(recipient);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "E-posta gonderilemedi: {Recipient}", recipient);
                result.FailedRecipients.Add((recipient, ex.Message));
            }
        }

        if (result.PartialSuccess)
            _logger.LogWarning("E-posta kısmen gönderildi: {Success}/{Total} başarılı",
                result.SucceededRecipients.Count, recipients.Count);

        return result;
    }

    public async Task<(bool success, string error)> TestConnectionAsync()
    {
        var config = await GetSmtpConfigAsync();
        if (config == null)
            return (false, "SMTP ayarlari yapilandirilmamis");

        try
        {
            using var client = new SmtpClient();
            client.Timeout = 10000;
            await client.ConnectAsync(config.Host, config.Port, config.UseSsl
                ? MailKit.Security.SecureSocketOptions.SslOnConnect
                : MailKit.Security.SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(config.Username, config.Password);
            await client.DisconnectAsync(true);
            return (true, "Baglanti basarili");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SMTP baglanti testi basarisiz");
            return (false, ex.Message);
        }
    }

    public async Task<(bool success, string message)> SendTestEmailAsync(string recipient, string requestedBy)
    {
        var config = await GetSmtpConfigAsync();
        if (config == null)
            return (false, "SMTP ayarlari yapilandirilmamis");

        if (string.IsNullOrWhiteSpace(recipient))
            return (false, "Test e-postasi icin alici adresi bulunamadi");

        var subject = $"[MudoSoft] SMTP Test E-postasi - {DateTime.Now:dd.MM.yyyy HH:mm}";
        var htmlBody = BuildTestEmailHtml(requestedBy, recipient, config);

        try
        {
            await SendSingleEmailAsync(config, recipient, subject, htmlBody);
            return (true, $"Test e-postasi gonderildi: {recipient}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Test e-postasi gonderilemedi: {Recipient}", recipient);
            return (false, ex.Message);
        }
    }

    public async Task<bool> SendEmailWithAttachmentAsync(string recipient, string subject, string htmlBody, byte[] attachment, string attachmentName)
    {
        var config = await GetSmtpConfigAsync();
        if (config == null)
        {
            _logger.LogWarning("SMTP yapilandirmasi bulunamadi, e-posta gonderilemedi");
            return false;
        }

        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(config.FromName, config.FromAddress));
            message.To.Add(MailboxAddress.Parse(recipient));
            message.Subject = subject;

            var bodyBuilder = new BodyBuilder { HtmlBody = htmlBody };
            bodyBuilder.Attachments.Add(attachmentName, attachment, ContentType.Parse("application/zip"));
            message.Body = bodyBuilder.ToMessageBody();

            using var client = new SmtpClient();
            client.Timeout = 30000;
            await client.ConnectAsync(config.Host, config.Port, config.UseSsl
                ? MailKit.Security.SecureSocketOptions.SslOnConnect
                : MailKit.Security.SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(config.Username, config.Password);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            _logger.LogInformation("Attachment e-posta gonderildi: {To} - {Subject} ({Size} KB)", recipient, subject, attachment.Length / 1024);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Attachment e-posta gonderilemedi: {Recipient}", recipient);
            return false;
        }
    }

    public async Task<(bool success, string error)> SendWithCcAsync(string to, IEnumerable<string> cc, string subject, string htmlBody, string plainTextBody)
    {
        var config = await GetSmtpConfigAsync();
        if (config == null)
            return (false, "SMTP yapılandırması bulunamadı");

        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(config.FromName, config.FromAddress));
            message.To.Add(MailboxAddress.Parse(to));
            foreach (var ccAddr in cc.Where(a => !string.IsNullOrWhiteSpace(a)).Distinct())
            {
                try { message.Cc.Add(MailboxAddress.Parse(ccAddr)); }
                catch (Exception ex) { _logger.LogWarning(ex, "CC parse hatası: {Cc}", ccAddr); }
            }
            message.Subject = subject;

            var bodyBuilder = new BodyBuilder
            {
                HtmlBody = htmlBody,
                TextBody = plainTextBody,
            };
            message.Body = bodyBuilder.ToMessageBody();

            using var client = new SmtpClient();
            client.Timeout = 15000;
            await client.ConnectAsync(config.Host, config.Port, config.UseSsl
                ? MailKit.Security.SecureSocketOptions.SslOnConnect
                : MailKit.Security.SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(config.Username, config.Password);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            _logger.LogInformation("Arıza maili gönderildi: To={To} CC={CcCount} Subject={Subject}",
                to, message.Cc.Count, subject);
            return (true, "");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Arıza maili gönderilemedi: {To}", to);
            return (false, ex.Message);
        }
    }

    private async Task SendSingleEmailAsync(SmtpConfig config, string to, string subject, string htmlBody)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(config.FromName, config.FromAddress));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;

        var bodyBuilder = new BodyBuilder { HtmlBody = htmlBody };
        message.Body = bodyBuilder.ToMessageBody();

        using var client = new SmtpClient();
        client.Timeout = 10000;
        await client.ConnectAsync(config.Host, config.Port, config.UseSsl
            ? MailKit.Security.SecureSocketOptions.SslOnConnect
            : MailKit.Security.SecureSocketOptions.StartTls);
        await client.AuthenticateAsync(config.Username, config.Password);
        await client.SendAsync(message);
        await client.DisconnectAsync(true);

        _logger.LogInformation("E-posta gonderildi: {To} - {Subject}", to, subject);
    }

    private async Task<SmtpConfig?> GetSmtpConfigAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MudoSoftDbContext>();

        var keys = new[] { "smtp:host", "smtp:port", "smtp:username", "smtp:password", "smtp:useSsl", "smtp:fromAddress", "smtp:fromName" };
        var settings = await db.AppSettings
            .Where(s => keys.Contains(s.Key))
            .ToDictionaryAsync(s => s.Key, s => s.Value);

        if (!settings.ContainsKey("smtp:host") || string.IsNullOrEmpty(settings["smtp:host"]))
            return null;

        var password = settings.GetValueOrDefault("smtp:password", "");
        if (!string.IsNullOrEmpty(password))
        {
            try { password = DecryptPassword(password); }
            catch { _logger.LogWarning("SMTP sifre cozulemedi"); return null; }
        }

        return new SmtpConfig
        {
            Host = settings.GetValueOrDefault("smtp:host", ""),
            Port = int.TryParse(settings.GetValueOrDefault("smtp:port", "587"), out var p) ? p : 587,
            Username = settings.GetValueOrDefault("smtp:username", ""),
            Password = password,
            UseSsl = settings.GetValueOrDefault("smtp:useSsl", "false") == "true",
            FromAddress = settings.GetValueOrDefault("smtp:fromAddress", ""),
            FromName = settings.GetValueOrDefault("smtp:fromName", "MudoSoft RMM")
        };
    }

    // SMTP sifre sifreleme/cozme (AES-256 + JWT key)
    public string EncryptPassword(string plainText)
    {
        using var aes = Aes.Create();
        var keyBytes = DeriveKey(_encryptionKey);
        aes.Key = keyBytes;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var encrypted = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        // IV + encrypted data birlestirilip base64 yapilir
        var result = new byte[aes.IV.Length + encrypted.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(encrypted, 0, result, aes.IV.Length, encrypted.Length);
        return Convert.ToBase64String(result);
    }

    private string DecryptPassword(string cipherText)
    {
        var fullCipher = Convert.FromBase64String(cipherText);
        using var aes = Aes.Create();
        var keyBytes = DeriveKey(_encryptionKey);
        aes.Key = keyBytes;

        var iv = new byte[16];
        Buffer.BlockCopy(fullCipher, 0, iv, 0, 16);
        aes.IV = iv;

        var cipherBytes = new byte[fullCipher.Length - 16];
        Buffer.BlockCopy(fullCipher, 16, cipherBytes, 0, cipherBytes.Length);

        using var decryptor = aes.CreateDecryptor();
        var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
        return Encoding.UTF8.GetString(plainBytes);
    }

    private static byte[] DeriveKey(string key)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(key));
    }

    private static string BuildTestEmailHtml(string requestedBy, string recipient, SmtpConfig config)
    {
        var safeRequestedBy = string.IsNullOrWhiteSpace(requestedBy) ? "Bilinmiyor" : requestedBy;

        return $@"
<div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto;'>
  <div style='background: #1e1b4b; color: white; padding: 16px 24px; border-radius: 8px 8px 0 0;'>
    <h2 style='margin: 0; font-size: 18px;'>SMTP Test E-postasi</h2>
  </div>
  <div style='background: #f8fafc; padding: 24px; border: 1px solid #e2e8f0; border-top: none; border-radius: 0 0 8px 8px;'>
    <p style='margin-top: 0; color: #0f172a;'>Bu ileti, MudoSoft RMM SMTP ayarlarinin gercek teslimatini dogrulamak icin gonderildi.</p>
    <table style='width: 100%; border-collapse: collapse;'>
      <tr><td style='padding: 8px 0; color: #64748b; width: 160px;'>Talep Eden:</td><td style='padding: 8px 0; font-weight: 600;'>{safeRequestedBy}</td></tr>
      <tr><td style='padding: 8px 0; color: #64748b;'>Alici:</td><td style='padding: 8px 0; font-weight: 600;'>{recipient}</td></tr>
      <tr><td style='padding: 8px 0; color: #64748b;'>Gonderen:</td><td style='padding: 8px 0; font-weight: 600;'>{config.FromName} &lt;{config.FromAddress}&gt;</td></tr>
      <tr><td style='padding: 8px 0; color: #64748b;'>Sunucu:</td><td style='padding: 8px 0; font-family: monospace;'>{config.Host}:{config.Port}</td></tr>
      <tr><td style='padding: 8px 0; color: #64748b;'>Zaman:</td><td style='padding: 8px 0;'>{DateTime.Now:dd.MM.yyyy HH:mm:ss}</td></tr>
    </table>
    <hr style='border: none; border-top: 1px solid #e2e8f0; margin: 16px 0;' />
    <p style='color: #94a3b8; font-size: 12px; margin: 0;'>Bu otomatik bir test iletisidir.</p>
  </div>
</div>";
    }

    public class SmtpConfig
    {
        public string Host { get; set; } = "";
        public int Port { get; set; } = 587;
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public bool UseSsl { get; set; }
        public string FromAddress { get; set; } = "";
        public string FromName { get; set; } = "MudoSoft RMM";
    }
}
