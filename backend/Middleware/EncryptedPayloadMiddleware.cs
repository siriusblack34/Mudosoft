using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using MudoSoft.Backend.Crypto;
using Mudosoft.Shared.Dtos;
using System; // Convert.FromBase64String için eklendi

namespace MudoSoft.Backend.Middleware;

public class EncryptedPayloadMiddleware
{
    private readonly RequestDelegate _next;

    private class AesKeyBundle
    {
        public string Key { get; set; } = default!;
        public string IV { get; set; } = default!;
    }

    public EncryptedPayloadMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext ctx, RsaKeyProvider rsa, AesEncryption aes) 
    {
        // ... (Bypass mantığı) ...

        // Şifreleme header yok → bypass
        if (!ctx.Request.Headers.TryGetValue("X-Encrypted", out var flag) ||
            flag != "1")
        {
            await _next(ctx);
            return;
        }

        ctx.Request.EnableBuffering();
        using var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8, leaveOpen: true);
        var bodyString = await reader.ReadToEndAsync();
        ctx.Request.Body.Position = 0;

        if (string.IsNullOrWhiteSpace(bodyString))
        {
            await _next(ctx);
            return;
        }

        EncryptedPayloadDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<EncryptedPayloadDto>(bodyString, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            
            if (dto == null || string.IsNullOrWhiteSpace(dto.EncryptedAesKey) || string.IsNullOrWhiteSpace(dto.EncryptedPayload))
            {
                await _next(ctx); 
                return;
            }
        }
        catch (JsonException)
        {
            await _next(ctx); 
            return;
        }

        try
        {
            // 1. RSA Private Key ile Base64 şifreli AES Key'i çöz
            var decryptedAesKeyBytes = rsa.Decrypt(Convert.FromBase64String(dto.EncryptedAesKey));
            
            // HATA ÇÖZÜMÜ: byte[]'den string'e çevrildi (JSON string için)
            var decryptedAesKeyJson = Encoding.UTF8.GetString(decryptedAesKeyBytes); 

            // 2. AES Key ve IV'yi JSON'dan çöz (Unpack)
            var keyBundle = JsonSerializer.Deserialize<AesKeyBundle>(decryptedAesKeyJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (keyBundle == null) throw new InvalidOperationException("Key bundle deserialization failed.");

            // Base64'ten byte dizisine dönüştürme
            var aesKeyBytes = Convert.FromBase64String(keyBundle.Key);
            var aesIVBytes = Convert.FromBase64String(keyBundle.IV);

            // 3. AES Key/IV ile şifrelenmiş asıl veriyi çöz (CS7036/CS1503 ÇÖZÜLDÜ)
            var decryptedJson = aes.Decrypt(dto.EncryptedPayload, aesKeyBytes, aesIVBytes);

            // 4. HTTP Body'yi çözülmüş JSON ile değiştir
            var newBodyBytes = Encoding.UTF8.GetBytes(decryptedJson);
            ctx.Request.Body = new MemoryStream(newBodyBytes);

        }
        catch (Exception)
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            await ctx.Response.WriteAsync("Decryption Failed: Invalid or Corrupted Payload.");
            return;
        }


        await _next(ctx);
    }
}