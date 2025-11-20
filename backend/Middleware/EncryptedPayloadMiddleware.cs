using System.Text;
using Microsoft.AspNetCore.Http;
using MudoSoft.Backend.Crypto;

namespace MudoSoft.Backend.Middleware;

public class EncryptedPayloadMiddleware
{
    private readonly RequestDelegate _next;

    public EncryptedPayloadMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext ctx, RsaKeyProvider rsa)
    {
        // Swagger, GET ve boş body bypass
        var path = ctx.Request.Path.Value?.ToLowerInvariant() ?? "";
        if (ctx.Request.Method == "GET" ||
            path.StartsWith("/swagger") ||
            ctx.Request.ContentLength is null ||
            ctx.Request.ContentLength == 0)
        {
            await _next(ctx);
            return;
        }

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

        // Base64 decode attempt
        byte[] encryptedBytes;
        try
        {
            encryptedBytes = Convert.FromBase64String(bodyString);
        }
        catch
        {
            await _next(ctx);
            return;
        }

        // RSA decrypt
        var decryptedJson = rsa.Decrypt(encryptedBytes);

        // Replace body
        var newBodyBytes = Encoding.UTF8.GetBytes(decryptedJson);
        ctx.Request.Body = new MemoryStream(newBodyBytes);

        await _next(ctx);
    }
}
