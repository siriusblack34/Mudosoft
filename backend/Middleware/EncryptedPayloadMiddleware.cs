using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using MudoSoft.Backend.Crypto;
using Mudosoft.Shared.Dtos;

namespace MudoSoft.Backend.Middleware;

public class EncryptedPayloadMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<EncryptedPayloadMiddleware> _logger;

    private class AesKeyBundle
    {
        public string Key { get; set; } = default!;
        public string IV { get; set; } = default!;
    }

    public EncryptedPayloadMiddleware(RequestDelegate next, ILogger<EncryptedPayloadMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext ctx, RsaKeyProvider rsa, AesEncryption aes)
    {
        // 🔹 Şifreli değilse geç
        if (!ctx.Request.Headers.TryGetValue("X-Encrypted", out var flag) || flag != "1")
        {
            await _next(ctx);
            return;
        }

        ctx.Request.EnableBuffering();

        string bodyString;
        using (var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8, leaveOpen: true))
        {
            bodyString = await reader.ReadToEndAsync();
        }

        if (string.IsNullOrWhiteSpace(bodyString))
        {
            ctx.Request.Body.Position = 0;
            await _next(ctx);
            return;
        }

        // ✅ TEK VE DOĞRU MODEL
        EncryptedPayloadDto payload;
        try
        {
            payload = JsonSerializer.Deserialize<EncryptedPayloadDto>(
                bodyString,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            )!;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Middleware: JSON Deserialization failed.");
            ctx.Request.Body.Position = 0;
            await _next(ctx);
            return;
        }

        try
        {
            // 1️⃣ RSA → AES key
            var decryptedAesKeyBytes =
                rsa.Decrypt(Convert.FromBase64String(payload.EncryptedAesKey));

            var decryptedAesKeyJson =
                Encoding.UTF8.GetString(decryptedAesKeyBytes);

            var keyBundle = JsonSerializer.Deserialize<AesKeyBundle>(
                decryptedAesKeyJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            )!;

            var aesKeyBytes = Convert.FromBase64String(keyBundle.Key);
            var aesIVBytes = Convert.FromBase64String(keyBundle.IV);

            // 1.5️⃣ HMAC integrity check (if provided)
            if (!string.IsNullOrEmpty(payload.Hmac))
            {
                using var hmac = new HMACSHA256(aesKeyBytes);
                var payloadBytes = Convert.FromBase64String(payload.EncryptedPayload);
                var computedHmac = Convert.ToBase64String(hmac.ComputeHash(payloadBytes));
                if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(computedHmac),
                    Encoding.UTF8.GetBytes(payload.Hmac)))
                {
                    _logger.LogWarning("Middleware: HMAC verification failed — payload may have been tampered.");
                    ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await ctx.Response.WriteAsync("Integrity check failed");
                    return;
                }
            }

            // 2️⃣ AES → JSON
            var decryptedJson =
                aes.Decrypt(payload.EncryptedPayload, aesKeyBytes, aesIVBytes);

            // 3️⃣ MVC için body reset
            var newBodyBytes = Encoding.UTF8.GetBytes(decryptedJson);
            ctx.Request.Body = new MemoryStream(newBodyBytes);
            ctx.Request.ContentLength = newBodyBytes.Length;
            ctx.Request.ContentType = "application/json";
            ctx.Request.Body.Position = 0;
        }
        catch (Exception ex)
        {
             _logger.LogError(ex, "Middleware: Decryption/Replacement logic failed.");
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            await ctx.Response.WriteAsync("Decryption Failed");
            return;
        }

        await _next(ctx);
    }
}
