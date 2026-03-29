using System.IdentityModel.Tokens.Jwt;
using System.Net.WebSockets;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MudoSoft.Backend.Data;
using MudoSoft.Backend.Models;
using MudoSoft.Backend.Services;

namespace MudoSoft.Backend.Middleware;

public static class VncWebSocketExtensions
{
    public static IApplicationBuilder UseVncWebSocket(
        this IApplicationBuilder app, string path = "/ws/vnc")
    {
        app.Use(async (context, next) =>
        {
            if (context.Request.Path == path && context.WebSockets.IsWebSocketRequest)
            {
                await VncWebSocketHandler.HandleAsync(context);
                return;
            }
            await next();
        });
        return app;
    }
}

/// <summary>
/// WebSocket-to-TCP proxy for VNC connections with SERVER-SIDE authentication.
/// 
/// Flow:
/// 1. Backend opens TCP to VNC server
/// 2. Backend performs RFB handshake + VNC DES auth on TCP side
/// 3. Backend performs ClientInit/ServerInit exchange to get desktop info
/// 4. Browser connects via WebSocket
/// 5. Backend sends a synthetic RFB handshake to the browser (with None auth)
/// 6. Browser does ClientInit, backend sends cached ServerInit
/// 7. After both handshakes complete, bidirectional relay kicks in
/// </summary>
internal static class VncWebSocketHandler
{
    public static async Task HandleAsync(HttpContext context)
    {
        var logger = context.RequestServices.GetRequiredService<ILogger<VncSessionManager>>();

        // 1. Validate JWT from query string
        var token = context.Request.Query["access_token"].FirstOrDefault();
        if (string.IsNullOrEmpty(token))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Missing access_token");
            return;
        }

        var config = context.RequestServices.GetRequiredService<IConfiguration>();
        var jwtUsername = ValidateToken(token, config);
        if (jwtUsername == null)
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Invalid or expired token");
            return;
        }

        // 2. Get deviceId from query string
        var deviceId = context.Request.Query["deviceId"].FirstOrDefault();
        if (string.IsNullOrEmpty(deviceId))
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsync("Missing deviceId");
            return;
        }

        // 3. Look up device from database
        var db = context.RequestServices.GetRequiredService<MudoSoftDbContext>();
        var device = await db.Devices.AsNoTracking()
            .Where(d => d.Id == deviceId)
            .Select(d => new { d.IpAddress, d.Hostname, d.Online, d.VncInstalled, d.VncPassword, d.VncPort })
            .FirstOrDefaultAsync();

        string? targetIp = device?.IpAddress;
        if (targetIp == null)
        {
            var storeDevice = await db.StoreDevices.AsNoTracking()
                .Where(sd => sd.DeviceId == deviceId)
                .Select(sd => new { sd.CalculatedIpAddress })
                .FirstOrDefaultAsync();
            targetIp = storeDevice?.CalculatedIpAddress;
        }

        if (string.IsNullOrEmpty(targetIp))
        {
            context.Response.StatusCode = 404;
            await context.Response.WriteAsync("Device not found or has no IP address");
            return;
        }

        if (device == null || !device.VncInstalled || string.IsNullOrEmpty(device.VncPassword))
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsync("VNC is not installed on this device");
            return;
        }

        var vncPassword = device.VncPassword;
        var vncPort = device.VncPort > 0 ? device.VncPort : 5900;

        // 4. Open raw TCP connection to VNC server
        var sessionManager = context.RequestServices.GetRequiredService<VncSessionManager>();
        VncSession session;
        try
        {
            session = await sessionManager.CreateSessionAsync(deviceId, targetIp, jwtUsername, vncPort);
        }
        catch (InvalidOperationException ex)
        {
            context.Response.StatusCode = 503;
            await context.Response.WriteAsync(ex.Message);
            return;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[VNC-WS] Failed to connect to VNC for {DeviceId}", deviceId);
            context.Response.StatusCode = 502;
            await context.Response.WriteAsync("VNC sunucusuna bağlanılamadı");
            return;
        }

        // 5. Perform server-side VNC authentication on the TCP connection
        logger.LogInformation("[VNC-WS] Performing server-side VNC authentication for device {DeviceId}...", deviceId);
        
        var (authSuccess, authError) = await VncAuthenticator.AuthenticateAsync(
            session.TcpStream, vncPassword, logger);

        if (!authSuccess)
        {
            logger.LogWarning("[VNC-WS] Server-side VNC auth failed: {Error}", authError);
            sessionManager.RemoveSession(session.SessionId);
            context.Response.StatusCode = 502;
            await context.Response.WriteAsync($"VNC authentication failed: {authError}");
            return;
        }

        // 6. Do ClientInit/ServerInit on the VNC side to get desktop info
        var serverInitData = await VncAuthenticator.PerformClientServerInitAsync(
            session.TcpStream, logger);

        if (serverInitData == null)
        {
            logger.LogWarning("[VNC-WS] Failed to get ServerInit from VNC");
            sessionManager.RemoveSession(session.SessionId);
            context.Response.StatusCode = 502;
            await context.Response.WriteAsync("VNC initialization failed");
            return;
        }

        logger.LogInformation("[VNC-WS] VNC authenticated and initialized. Accepting WebSocket...");

        // 7. Accept WebSocket from browser
        WebSocket ws;
        try
        {
            var requestedProtocol = context.WebSockets.WebSocketRequestedProtocols.Contains("binary")
                ? "binary" : null;
            ws = await context.WebSockets.AcceptWebSocketAsync(requestedProtocol);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[VNC-WS] Failed to accept WebSocket");
            sessionManager.RemoveSession(session.SessionId);
            return;
        }

        logger.LogInformation("[VNC-WS] WebSocket accepted. Sending synthetic RFB handshake to browser...");

        // 8. Send synthetic RFB handshake to browser (None auth)
        try
        {
            // Server version
            var versionBytes = Encoding.ASCII.GetBytes("RFB 003.008\n");
            await ws.SendAsync(versionBytes, WebSocketMessageType.Binary, true, CancellationToken.None);

            // Read client version from browser
            var clientVersionBuf = new byte[12];
            var receiveResult = await ws.ReceiveAsync(clientVersionBuf, CancellationToken.None);
            logger.LogDebug("[VNC-WS] Browser sent version: {Hex}", 
                Encoding.ASCII.GetString(clientVersionBuf, 0, receiveResult.Count).Trim());

            // Send security types: 1 type available = None (1)
            await ws.SendAsync(new byte[] { 1, 1 }, WebSocketMessageType.Binary, true, CancellationToken.None);

            // Read browser's security type selection
            var secTypeBuf = new byte[1];
            receiveResult = await ws.ReceiveAsync(secTypeBuf, CancellationToken.None);
            logger.LogDebug("[VNC-WS] Browser selected security type: {Type}", secTypeBuf[0]);

            // Send SecurityResult: OK (0)
            await ws.SendAsync(new byte[] { 0, 0, 0, 0 }, WebSocketMessageType.Binary, true, CancellationToken.None);

            // Read ClientInit from browser (1 byte: shared flag)
            var clientInitBuf = new byte[1];
            receiveResult = await ws.ReceiveAsync(clientInitBuf, CancellationToken.None);
            logger.LogDebug("[VNC-WS] Browser ClientInit shared={Shared}", clientInitBuf[0]);

            // Send cached ServerInit to browser
            await ws.SendAsync(serverInitData, WebSocketMessageType.Binary, true, CancellationToken.None);
            logger.LogInformation("[VNC-WS] Synthetic RFB handshake complete. Starting bidirectional relay.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[VNC-WS] Failed during synthetic RFB handshake");
            sessionManager.RemoveSession(session.SessionId);
            ws.Dispose();
            return;
        }

        // Log session start
        await LogSessionStartAsync(context, session.SessionId, deviceId, jwtUsername, targetIp);

        // 9. Bidirectional relay (post-auth, both sides are ready for RFB messages)
        string disconnectReason = "clean";
        try
        {
            disconnectReason = await RelayAsync(ws, session, logger, CancellationToken.None);
        }
        finally
        {
            await LogSessionEndAsync(context, session.SessionId, session.StartedAt, disconnectReason);
            sessionManager.RemoveSession(session.SessionId);
            if (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived)
            {
                try
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Session ended", CancellationToken.None);
                }
                catch { }
            }
            ws.Dispose();
            logger.LogInformation("[VNC-WS] Session ended for device {DeviceId}", deviceId);
        }
    }

    /// <summary>
    /// Bidirectional relay: WebSocket (binary) ↔ TCP stream.
    /// Returns "clean" if client closed normally, "error" otherwise.
    /// </summary>
    private static async Task<string> RelayAsync(
        WebSocket ws, VncSession session, ILogger logger, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = cts.Token;
        bool cleanClose = false;

        // WebSocket → TCP (browser input → VNC server)
        var wsToTcp = Task.Run(async () =>
        {
            var buffer = new byte[16384];
            try
            {
                while (ws.State == WebSocketState.Open && !token.IsCancellationRequested)
                {
                    var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        cleanClose = true;
                        break;
                    }

                    if (result.Count > 0 && session.TcpStream.CanWrite)
                    {
                        await session.TcpStream.WriteAsync(buffer.AsMemory(0, result.Count), token);
                        await session.TcpStream.FlushAsync(token);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException) { }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "[VNC-WS] WS→TCP relay error");
            }
            finally
            {
                cts.Cancel();
            }
        }, token);

        // TCP → WebSocket (VNC server output → browser)
        var tcpToWs = Task.Run(async () =>
        {
            var buffer = new byte[16384];
            try
            {
                while (session.TcpClient.Connected && ws.State == WebSocketState.Open && !token.IsCancellationRequested)
                {
                    int bytesRead = await session.TcpStream.ReadAsync(buffer, token);
                    if (bytesRead == 0) break;

                    await ws.SendAsync(
                        new ArraySegment<byte>(buffer, 0, bytesRead),
                        WebSocketMessageType.Binary,
                        true,
                        token);
                }
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException) { }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "[VNC-WS] TCP→WS relay error");
            }
            finally
            {
                cts.Cancel();
            }
        }, token);

        await Task.WhenAny(wsToTcp, tcpToWs);
        cts.Cancel();

        try { await Task.WhenAll(wsToTcp, tcpToWs); }
        catch (OperationCanceledException) { }

        return cleanClose ? "clean" : "error";
    }

    private static async Task LogSessionStartAsync(
        HttpContext context, string sessionId, string deviceId,
        string username, string targetIp)
    {
        try
        {
            var scopeFactory = context.RequestServices.GetRequiredService<IServiceScopeFactory>();
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MudoSoftDbContext>();
            db.VncSessionLogs.Add(new VncSessionLog
            {
                SessionId    = sessionId,
                DeviceId     = deviceId,
                Username     = username,
                TargetIp     = targetIp,
                StartedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
        catch { /* Non-critical — don't break the relay */ }
    }

    private static async Task LogSessionEndAsync(
        HttpContext context, string sessionId, DateTime startedAt, string reason)
    {
        try
        {
            var scopeFactory = context.RequestServices.GetRequiredService<IServiceScopeFactory>();
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MudoSoftDbContext>();
            var log = await db.VncSessionLogs.FirstOrDefaultAsync(l => l.SessionId == sessionId);
            if (log != null)
            {
                log.EndedAtUtc       = DateTime.UtcNow;
                log.DurationSeconds  = (int)(DateTime.UtcNow - startedAt).TotalSeconds;
                log.DisconnectReason = reason;
                await db.SaveChangesAsync();
            }
        }
        catch { /* Non-critical */ }
    }

    private static string? ValidateToken(string token, IConfiguration config)
    {
        try
        {
            var jwtKeyFromConfig = config["Jwt:Key"];
            var jwtKeyFromEnv = Environment.GetEnvironmentVariable("JWT_SECRET_KEY");

            if (!string.IsNullOrEmpty(jwtKeyFromConfig) && jwtKeyFromConfig.StartsWith("${"))
                jwtKeyFromConfig = null;

            var jwtKey = jwtKeyFromEnv ?? jwtKeyFromConfig;
            if (string.IsNullOrEmpty(jwtKey)) return null;

            var jwtIssuer = config["Jwt:Issuer"] ?? "MudoSoft";
            var jwtAudience = config["Jwt:Audience"] ?? "MudoSoftUsers";

            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtIssuer,
                ValidAudience = jwtAudience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                ClockSkew = TimeSpan.Zero
            }, out _);

            return principal.Identity?.Name
                ?? principal.FindFirst("sub")?.Value
                ?? principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? "unknown";
        }
        catch
        {
            return null;
        }
    }
}
