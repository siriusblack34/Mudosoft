using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestra.Agent.Interfaces;
using Orchestra.Agent.Models;
using Orchestra.Shared.Dtos;

namespace Orchestra.Agent.Services;

/// <summary>
/// 🔒 Faz 2 (K-2/K-5): Backend komut imzalarını doğrular + cihaz enrollment.
/// - İlk açılışta cihaz RSA anahtar çifti üretir (DPAPI ile saklar) ve /api/agent/enroll çağırır.
/// - Enroll yanıtındaki (ya da /api/Security/public-key'den) backend public key'i PİNLER (TOFU).
/// - Her komutu doğrular: imza (RSA-SHA256/PKCS1) + DeviceId==self + Seq>sonGörülen (replay) + süre penceresi.
/// Doğrulama başarısızsa komut ÇALIŞTIRILMAZ. Backend AYNI kanonik formatı imzalar:
///   "{Id}|{DeviceId}|{(int)Type}|{Payload}|{Seq}|{IssuedAtUtc:O}|{ExpiresAtUtc:O}"
/// </summary>
public sealed class CommandSecurityService
{
    private readonly AgentConfig _config;
    private readonly ILogger<CommandSecurityService> _logger;
    private readonly IDeviceIdentityProvider _identity;
    private readonly HttpClient _http;

    private static readonly string DataDir = @"C:\ProgramData\Orchestra\agent";
    private static readonly string DeviceKeyFile = Path.Combine(DataDir, "device_key.dat");
    private static readonly string BackendPubFile = Path.Combine(DataDir, "backend_pub.dat");
    private static readonly string SeqFile = Path.Combine(DataDir, "last_seq.dat");
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Orchestra.Agent.KeyStore.v1");

    private readonly object _lock = new();
    private RSA? _deviceKey;
    private RSA? _backendPub;
    private long _lastSeq = -1;

    public CommandSecurityService(
        IOptions<AgentConfig> config,
        ILogger<CommandSecurityService> logger,
        IDeviceIdentityProvider identity,
        IHttpClientFactory httpFactory)
    {
        _config = config.Value;
        _logger = logger;
        _identity = identity;
        _http = httpFactory.CreateClient();
        if (!string.IsNullOrWhiteSpace(_config.BackendUrl))
            _http.BaseAddress = new Uri(_config.BackendUrl);

        try { Directory.CreateDirectory(DataDir); } catch (Exception ex) { _logger.LogWarning(ex, "Veri klasörü oluşturulamadı"); }
        LoadState();
    }

    public bool IsEnrolled { get { lock (_lock) return _backendPub != null; } }

    private void LoadState()
    {
        lock (_lock)
        {
            // 1) Cihaz özel anahtarı — yoksa üret + DPAPI ile sakla
            try
            {
                if (File.Exists(DeviceKeyFile))
                {
                    var xml = Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(DeviceKeyFile), Entropy, DataProtectionScope.LocalMachine));
                    _deviceKey = RSA.Create();
                    _deviceKey.FromXmlString(xml);
                }
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Cihaz anahtarı yüklenemedi, yenisi üretilecek"); _deviceKey = null; }

            if (_deviceKey == null)
            {
                _deviceKey = RSA.Create(2048);
                try { File.WriteAllBytes(DeviceKeyFile, ProtectedData.Protect(Encoding.UTF8.GetBytes(_deviceKey.ToXmlString(true)), Entropy, DataProtectionScope.LocalMachine)); }
                catch (Exception ex) { _logger.LogWarning(ex, "Cihaz anahtarı kaydedilemedi"); }
            }

            // 2) Pinlenmiş backend public key
            try
            {
                if (File.Exists(BackendPubFile))
                {
                    var xml = Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(BackendPubFile), Entropy, DataProtectionScope.LocalMachine));
                    _backendPub = RSA.Create();
                    _backendPub.FromXmlString(xml);
                }
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Pinli backend key yüklenemedi"); _backendPub = null; }

            // 3) Son görülen Seq (replay)
            try
            {
                if (File.Exists(SeqFile))
                {
                    var s = Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(SeqFile), Entropy, DataProtectionScope.LocalMachine));
                    if (long.TryParse(s, out var v)) _lastSeq = v;
                }
            }
            catch { /* ilk açılış */ }
        }
    }

    /// <summary>Backend public key pinli değilse enroll et / key'i çek ve pinle. Idempotent.</summary>
    public async Task EnsureEnrolledAsync(CancellationToken ct)
    {
        if (IsEnrolled) return;

        var deviceId = _identity.GetDeviceId();
        string devicePub;
        lock (_lock) { devicePub = _deviceKey!.ToXmlString(false); }

        string? backendPubXml = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(_config.BootstrapApiKey))
            {
                var resp = await _http.PostAsJsonAsync("api/agent/enroll",
                    new { deviceId, publicKey = devicePub, bootstrapApiKey = _config.BootstrapApiKey }, ct);
                if (resp.IsSuccessStatusCode)
                {
                    var body = await resp.Content.ReadFromJsonAsync<EnrollResponse>(cancellationToken: ct);
                    backendPubXml = body?.backendPublicKey;
                    _logger.LogInformation("✅ Enroll başarılı: {DeviceId}", deviceId);
                }
                else
                {
                    _logger.LogWarning("Enroll reddedildi ({Status}) — key'i public-key endpoint'inden çekeceğim", resp.StatusCode);
                }
            }

            // Enroll key vermediyse public-key endpoint'inden çek (TOFU pin)
            if (string.IsNullOrWhiteSpace(backendPubXml))
                backendPubXml = (await _http.GetStringAsync("api/Security/public-key", ct))?.Trim();

            if (!string.IsNullOrWhiteSpace(backendPubXml))
            {
                var rsa = RSA.Create();
                rsa.FromXmlString(backendPubXml);
                lock (_lock) { _backendPub = rsa; }
                try { File.WriteAllBytes(BackendPubFile, ProtectedData.Protect(Encoding.UTF8.GetBytes(backendPubXml), Entropy, DataProtectionScope.LocalMachine)); }
                catch (Exception ex) { _logger.LogWarning(ex, "Backend key pinlenemedi (diske)"); }
                _logger.LogInformation("🔑 Backend public key pinlendi.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Enrollment / key pinning başarısız — komutlar pinlenene kadar reddedilecek");
        }
    }

    /// <summary>Komutu doğrula. Geçersizse false + reason (komut çalıştırılmamalı).</summary>
    public bool TryVerify(CommandDto cmd, out string reason)
    {
        var deviceId = _identity.GetDeviceId();

        RSA? backend;
        lock (_lock) { backend = _backendPub; }
        if (backend == null) { reason = "backend key not pinned"; return false; }
        if (string.IsNullOrEmpty(cmd.Signature)) { reason = "missing signature"; return false; }
        if (!string.Equals(cmd.DeviceId, deviceId, StringComparison.Ordinal)) { reason = "deviceId mismatch"; return false; }
        if (cmd.Seq is null) { reason = "missing seq"; return false; }
        if (cmd.IssuedAtUtc is null || cmd.ExpiresAtUtc is null) { reason = "missing timestamps"; return false; }

        var now = DateTime.UtcNow;
        if (now > cmd.ExpiresAtUtc.Value) { reason = "expired"; return false; }
        if (cmd.IssuedAtUtc.Value > now.AddMinutes(2)) { reason = "issued in future"; return false; }

        byte[] sig;
        try { sig = Convert.FromBase64String(cmd.Signature); }
        catch { reason = "bad signature encoding"; return false; }

        bool ok;
        lock (_lock) { ok = backend.VerifyData(CanonicalBytes(cmd), sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1); }
        if (!ok) { reason = "signature invalid"; return false; }

        // Replay: Seq monotonik artmalı
        lock (_lock)
        {
            if (cmd.Seq.Value <= _lastSeq) { reason = $"replay (seq {cmd.Seq} <= {_lastSeq})"; return false; }
            _lastSeq = cmd.Seq.Value;
            try { File.WriteAllBytes(SeqFile, ProtectedData.Protect(Encoding.UTF8.GetBytes(_lastSeq.ToString()), Entropy, DataProtectionScope.LocalMachine)); }
            catch (Exception ex) { _logger.LogWarning(ex, "Seq kaydedilemedi"); }
        }

        reason = "";
        return true;
    }

    /// <summary>
    /// 🔒 Faz 2 (K-3): imzalı update manifest doğrulaması. Backend "{version}|{sha256}|{url}" imzalar.
    /// Geçersizse update UYGULANMAMALI.
    /// </summary>
    public bool VerifyManifest(string? version, string? sha256, string? url, string? sigB64, out string reason)
    {
        RSA? backend;
        lock (_lock) { backend = _backendPub; }
        if (backend == null) { reason = "backend key not pinned"; return false; }
        if (string.IsNullOrEmpty(version) || string.IsNullOrEmpty(sha256) || string.IsNullOrEmpty(url) || string.IsNullOrEmpty(sigB64))
        { reason = "manifest eksik alan"; return false; }

        byte[] sig;
        try { sig = Convert.FromBase64String(sigB64); }
        catch { reason = "bad signature encoding"; return false; }

        var canonical = Encoding.UTF8.GetBytes($"{version}|{sha256}|{url}");
        bool ok;
        lock (_lock) { ok = backend.VerifyData(canonical, sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1); }
        reason = ok ? "" : "signature invalid";
        return ok;
    }

    // Backend (AgentService.CanonicalCommandBytes) ile BİREBİR aynı olmalı.
    private static byte[] CanonicalBytes(CommandDto c) =>
        Encoding.UTF8.GetBytes($"{c.Id}|{c.DeviceId}|{(int)c.Type}|{c.Payload ?? ""}|{c.Seq}|{c.IssuedAtUtc:O}|{c.ExpiresAtUtc:O}");

    private sealed class EnrollResponse { public string? backendPublicKey { get; set; } }
}
