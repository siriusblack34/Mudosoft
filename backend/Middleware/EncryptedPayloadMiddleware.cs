using System.Text;
using System.Text.Json;
using MudoSoft.Backend.Crypto;
using MudoSoft.Backend.Models;

namespace MudoSoft.Backend.Middleware;

public class EncryptedPayloadMiddleware
{
    private readonly RequestDelegate _next;
    public EncryptedPayloadMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext ctx, RsaKeyProvider rsa)
    {
        if (!ctx.Request.Path.StartsWithSegments("/api/agent"))
        {
            await _next(ctx);
            return;
        }

        ctx.Request.EnableBuffering();
        using var reader = new StreamReader(ctx.Request.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        ctx.Request.Body.Position = 0;

        if (string.IsNullOrWhiteSpace(body))
        {
            await _next(ctx);
            return;
        }

        var enc = JsonSerializer.Deserialize<EncryptedPayload>(body);

        var aesKey = rsa.Decrypt(Convert.FromBase64String(enc!.EncryptedKey));
        var iv = Convert.FromBase64String(enc.IV);
        var cipher = Convert.FromBase64String(enc.Data);

        var plain = AesEncryption.Decrypt(cipher, aesKey, iv);
        var json = Encoding.UTF8.GetString(plain);

        ctx.Items["DecryptedBody"] = json;

        var originalBody = ctx.Request.Body;
        var newBody = new MemoryStream(Encoding.UTF8.GetBytes(json));
        ctx.Request.Body = newBody;

        await _next(ctx);
    }
}
