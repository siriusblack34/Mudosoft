using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Orchestra.Backend.Services;

/// <summary>
/// Performs VNC/RFB authentication server-side over an already-open TCP stream.
/// After calling AuthenticateAsync, the stream is past the security handshake
/// and ready for ClientInit/ServerInit and framebuffer operations.
/// </summary>
public static class VncAuthenticator
{
    /// <summary>
    /// Perform the full RFB version exchange + security negotiation + VNC DES auth
    /// on the given TCP stream. Returns (success, serverInitData) where serverInitData
    /// is null on failure.
    /// </summary>
    public static async Task<(bool Success, string? Error)> AuthenticateAsync(
        NetworkStream stream, string password, ILogger logger, CancellationToken ct = default)
    {
        try
        {
            // 1. Read server version (12 bytes: "RFB 003.008\n")
            var versionBuf = new byte[12];
            int totalRead = 0;
            while (totalRead < 12)
            {
                int n = await stream.ReadAsync(versionBuf.AsMemory(totalRead, 12 - totalRead), ct);
                if (n == 0) return (false, "VNC server closed connection during version exchange");
                totalRead += n;
            }
            var serverVersion = Encoding.ASCII.GetString(versionBuf).Trim();
            logger.LogInformation("[VNC-Auth] Server version: {Version}", serverVersion);

            // 2. Send client version
            await stream.WriteAsync(Encoding.ASCII.GetBytes("RFB 003.008\n"), ct);
            await stream.FlushAsync(ct);

            // 3. Read security types
            var numTypesBuf = new byte[1];
            await ReadExactAsync(stream, numTypesBuf, 1, ct);
            int numTypes = numTypesBuf[0];

            if (numTypes == 0)
            {
                // Connection rejected — read error message
                var errLenBuf = new byte[4];
                await ReadExactAsync(stream, errLenBuf, 4, ct);
                int errLen = (errLenBuf[0] << 24) | (errLenBuf[1] << 16) | (errLenBuf[2] << 8) | errLenBuf[3];
                if (errLen > 0 && errLen < 4096)
                {
                    var errBuf = new byte[errLen];
                    await ReadExactAsync(stream, errBuf, errLen, ct);
                    var errMsg = Encoding.UTF8.GetString(errBuf);
                    logger.LogWarning("[VNC-Auth] Connection rejected: {Error}", errMsg);
                    return (false, $"VNC connection rejected: {errMsg}");
                }
                return (false, "VNC connection rejected");
            }

            var typesBuf = new byte[numTypes];
            await ReadExactAsync(stream, typesBuf, numTypes, ct);
            var typesList = typesBuf.Select(b => (int)b).ToList();
            logger.LogInformation("[VNC-Auth] Security types: {Types}", string.Join(", ", typesList));

            // Prefer VNC Authentication (type 2)
            if (typesList.Contains(2))
            {
                // Select VNC Auth
                await stream.WriteAsync(new byte[] { 2 }, ct);
                await stream.FlushAsync(ct);

                // Read 16-byte challenge
                var challenge = new byte[16];
                await ReadExactAsync(stream, challenge, 16, ct);
                // 🔒 SECURITY (Y-6): challenge / DES anahtarı / response ARTIK LOGLANMIYOR.
                // Bunlar loglandığında, bit-ters DES anahtarından düz-metin VNC parolası ve
                // challenge/response'tan offline 56-bit DES brute-force ile parola kurtarılabiliyordu.
                logger.LogDebug("[VNC-Auth] Challenge received ({Len} bytes)", challenge.Length);

                // Compute DES response
                var response = ComputeVncAuthResponse(password, challenge);

                // Send response
                await stream.WriteAsync(response, ct);
                await stream.FlushAsync(ct);

                // Read result (4 bytes, big-endian uint32: 0=OK, 1=failed)
                var resultBuf = new byte[4];
                await ReadExactAsync(stream, resultBuf, 4, ct);
                uint result = (uint)((resultBuf[0] << 24) | (resultBuf[1] << 16) | (resultBuf[2] << 8) | resultBuf[3]);

                if (result == 0)
                {
                    logger.LogInformation("[VNC-Auth] VNC Authentication SUCCESS");
                    return (true, null);
                }
                else
                {
                    // Read error message if available (RFB 003.008)
                    string errorDetail = "Authentication failed";
                    try
                    {
                        var errLenBuf2 = new byte[4];
                        await ReadExactAsync(stream, errLenBuf2, 4, ct);
                        int errLen2 = (errLenBuf2[0] << 24) | (errLenBuf2[1] << 16) | (errLenBuf2[2] << 8) | errLenBuf2[3];
                        if (errLen2 > 0 && errLen2 < 4096)
                        {
                            var errBuf2 = new byte[errLen2];
                            await ReadExactAsync(stream, errBuf2, errLen2, ct);
                            errorDetail = Encoding.UTF8.GetString(errBuf2);
                        }
                    }
                    catch { }

                    logger.LogWarning("[VNC-Auth] VNC Authentication FAILED: {Error}", errorDetail);
                    return (false, errorDetail);
                }
            }
            else if (typesList.Contains(1))
            {
                // None auth — select it
                await stream.WriteAsync(new byte[] { 1 }, ct);
                await stream.FlushAsync(ct);

                // RFB 003.008 expects SecurityResult even for None
                var resultBuf = new byte[4];
                await ReadExactAsync(stream, resultBuf, 4, ct);
                uint result = (uint)((resultBuf[0] << 24) | (resultBuf[1] << 16) | (resultBuf[2] << 8) | resultBuf[3]);

                if (result == 0)
                {
                    logger.LogInformation("[VNC-Auth] None auth accepted");
                    return (true, null);
                }
                return (false, "None auth rejected");
            }
            else
            {
                logger.LogWarning("[VNC-Auth] No supported security type found. Available: {Types}", string.Join(", ", typesList));
                return (false, $"No supported VNC security type (available: {string.Join(", ", typesList)})");
            }
        }
        catch (OperationCanceledException)
        {
            return (false, "VNC authentication timed out");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[VNC-Auth] Authentication error");
            return (false, $"VNC authentication error: {ex.Message}");
        }
    }

    /// <summary>
    /// After server-side auth succeeds, do ClientInit + ServerInit exchange
    /// and return the ServerInit data (which includes desktop size + pixel format + name).
    /// </summary>
    public static async Task<byte[]?> PerformClientServerInitAsync(
        NetworkStream stream, ILogger logger, CancellationToken ct = default)
    {
        try
        {
            // Send ClientInit: shared flag = 1 (allow other clients)
            await stream.WriteAsync(new byte[] { 1 }, ct);
            await stream.FlushAsync(ct);

            // Read ServerInit: 24 bytes fixed + variable-length desktop name
            var fixedPart = new byte[24];
            await ReadExactAsync(stream, fixedPart, 24, ct);

            int nameLen = (fixedPart[20] << 24) | (fixedPart[21] << 16) | (fixedPart[22] << 8) | fixedPart[23];
            if (nameLen < 0 || nameLen > 4096)
            {
                logger.LogWarning("[VNC-Auth] Invalid desktop name length: {Len}", nameLen);
                return null;
            }

            var nameBuf = new byte[nameLen];
            if (nameLen > 0)
                await ReadExactAsync(stream, nameBuf, nameLen, ct);

            int width = (fixedPart[0] << 8) | fixedPart[1];
            int height = (fixedPart[2] << 8) | fixedPart[3];
            var desktopName = Encoding.UTF8.GetString(nameBuf);
            logger.LogInformation("[VNC-Auth] Desktop: {Width}x{Height} - {Name}", width, height, desktopName);

            // Return full ServerInit (fixed + name)
            var serverInit = new byte[24 + nameLen];
            Array.Copy(fixedPart, 0, serverInit, 0, 24);
            Array.Copy(nameBuf, 0, serverInit, 24, nameLen);
            return serverInit;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[VNC-Auth] ClientInit/ServerInit error");
            return null;
        }
    }

    /// <summary>
    /// Compute the VNC DES challenge-response for a given password and 16-byte challenge.
    /// Uses VNC's bit-reversed DES key derivation.
    /// </summary>
    private static byte[] ComputeVncAuthResponse(string password, byte[] challenge)
    {
        // Standard RFB VNC auth: each byte of password is bit-reversed to form DES key.
        // TightVNC 2.x stores plaintext password bytes in registry, then applies this
        // same bit-reversal during auth → backend must do the same.
        var key = GetDESKey(password);

        using var des = System.Security.Cryptography.DES.Create();
        des.Mode = CipherMode.ECB;
        des.Padding = PaddingMode.None;
        des.Key = key;

        var response = new byte[16];
        using var encryptor = des.CreateEncryptor();
        encryptor.TransformBlock(challenge, 0, 8, response, 0);
        encryptor.TransformBlock(challenge, 8, 8, response, 8);

        return response;
    }

    internal static byte[] GetDESKey(string password)
    {
        var passBytes = Encoding.ASCII.GetBytes(password);
        var key = new byte[8];
        Array.Copy(passBytes, key, Math.Min(passBytes.Length, 8));
        // RFB spec: each bit of each password byte is reversed
        for (int i = 0; i < 8; i++)
        {
            byte b = key[i], r = 0;
            for (int bit = 0; bit < 8; bit++) { r = (byte)((r << 1) | (b & 1)); b >>= 1; }
            key[i] = r;
        }
        return key;
    }

    private static async Task ReadExactAsync(NetworkStream stream, byte[] buffer, int count, CancellationToken ct)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(totalRead, count - totalRead), ct);
            if (n == 0) throw new IOException($"VNC connection closed (read {totalRead}/{count} bytes)");
            totalRead += n;
        }
    }
}
