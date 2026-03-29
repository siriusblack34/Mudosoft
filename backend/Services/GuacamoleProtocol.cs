using System.Net.Sockets;
using System.Text;

namespace MudoSoft.Backend.Services;

/// <summary>
/// Guacamole protocol encoder/decoder.
/// Format: length-prefixed values separated by commas, terminated by semicolon.
/// Example: "6.select,3.rdp;"
/// </summary>
public static class GuacamoleProtocol
{
    /// <summary>
    /// Encode a Guacamole instruction from opcode and arguments.
    /// </summary>
    public static string EncodeInstruction(string opcode, params string[] args)
    {
        var sb = new StringBuilder();
        sb.Append($"{opcode.Length}.{opcode}");
        foreach (var arg in args)
        {
            var val = arg ?? "";
            sb.Append($",{val.Length}.{val}");
        }
        sb.Append(';');
        return sb.ToString();
    }

    /// <summary>
    /// Read a single complete instruction from a NetworkStream.
    /// Returns (opcode, args) or null on stream end.
    /// </summary>
    public static async Task<(string Opcode, string[] Args)?> ReadInstructionAsync(
        NetworkStream stream, CancellationToken ct = default)
    {
        var elements = new List<string>();
        var buffer = new StringBuilder();

        while (true)
        {
            // Read length digits
            var lengthStr = new StringBuilder();
            while (true)
            {
                int b = await ReadByteAsync(stream, ct);
                if (b == -1) return null;

                char c = (char)b;
                if (c == '.')
                    break;
                if (c == ',')
                    continue; // separator between elements — skip
                if (c == ';')
                {
                    // End of instruction
                    if (elements.Count > 0)
                        return (elements[0], elements.Skip(1).ToArray());
                    return null;
                }
                lengthStr.Append(c);
            }

            if (!int.TryParse(lengthStr.ToString(), out int length))
                return null;

            // Read exactly 'length' bytes of UTF-8 value
            var valueBytes = new byte[length];
            int totalRead = 0;
            while (totalRead < length)
            {
                int read = await stream.ReadAsync(valueBytes.AsMemory(totalRead, length - totalRead), ct);
                if (read == 0) return null;
                totalRead += read;
            }

            elements.Add(Encoding.UTF8.GetString(valueBytes));

            // Read next char: comma (more elements) or semicolon (end)
            int next = await ReadByteAsync(stream, ct);
            if (next == -1) return null;
            if ((char)next == ';')
            {
                if (elements.Count > 0)
                    return (elements[0], elements.Skip(1).ToArray());
                return null;
            }
            // If comma, continue to next element
        }
    }

    /// <summary>
    /// Build the RDP connect instruction args based on guacd's expected arg names.
    /// </summary>
    public static string BuildConnectInstruction(
        string[] expectedArgNames,
        Dictionary<string, string> rdpParams)
    {
        var args = new string[expectedArgNames.Length];
        for (int i = 0; i < expectedArgNames.Length; i++)
        {
            args[i] = rdpParams.TryGetValue(expectedArgNames[i], out var val) ? val : "";
        }
        return EncodeInstruction("connect", args);
    }

    /// <summary>
    /// Get VNC connection parameters for viewing the existing active session.
    /// </summary>
    public static Dictionary<string, string> GetVncParams(
        string hostname, int port, string password,
        int width, int height, int dpi)
    {
        return new Dictionary<string, string>
        {
            ["hostname"] = hostname,
            ["port"] = port.ToString(),
            ["password"] = password,
            ["width"] = width.ToString(),
            ["height"] = height.ToString(),
            ["dpi"] = dpi.ToString(),
            ["color-depth"] = "16",
            ["swap-red-blue"] = "false",
            ["cursor"] = "remote",        // Show remote cursor
            ["read-only"] = "false",       // Allow interaction
            ["clipboard-encoding"] = "UTF-8",
            ["disable-copy"] = "false",
            ["disable-paste"] = "false",
            ["dest-host"] = "",
            ["dest-port"] = "",
            ["enable-audio"] = "false",
            ["audio-servername"] = "",
        };
    }

    private static async Task<int> ReadByteAsync(NetworkStream stream, CancellationToken ct)
    {
        var buf = new byte[1];
        int read = await stream.ReadAsync(buf.AsMemory(0, 1), ct);
        return read == 0 ? -1 : buf[0];
    }
}
