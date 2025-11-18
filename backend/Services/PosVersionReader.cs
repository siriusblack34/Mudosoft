using System.Text.RegularExpressions;

namespace MudoSoft.Backend.Services
{
    public class PosVersionReader : IPosVersionReader
    {
        public async Task<string?> GetVersion(string ip)
        {
            try
            {
                string geniusPath  = $"\\\\{ip}\\c$\\Geniuspos\\parameters.bat";
                string fashionPath = $"\\\\{ip}\\c$\\FASHION\\parameters.bat";

                string? path = null;

                if (File.Exists(geniusPath))
                    path = geniusPath;
                else if (File.Exists(fashionPath))
                    path = fashionPath;

                if (path == null)
                {
                    Console.WriteLine($"[POS] parameters.bat NOT FOUND on {ip}");
                    return null;
                }

                Console.WriteLine($"[POS] Reading: {path}");

                var text = await File.ReadAllTextAsync(path);

                // 1️⃣ Önce FASHION_JAR satırını oku (öncelik bu)
                var jarMatch = Regex.Match(
                    text,
                    @"set\s+FASHION_JAR\s*=\s*(.+)",
                    RegexOptions.IgnoreCase
                );

                if (jarMatch.Success)
                {
                    var rawJar = jarMatch.Groups[1].Value.Trim();
                    // fashion2701_751.jar  -> fashion2701_751
                    rawJar = rawJar.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
                        ? rawJar[..^4]
                        : rawJar;

                    Console.WriteLine($"[POS] FASHION_JAR found on {ip}: {rawJar}");
                    return rawJar; // 🔥 Senin istediğin değer bu
                }

                // 2️⃣ FASHION_JAR yoksa fallback: JPOS_VER
                var match = Regex.Match(
                    text,
                    @"set\s+JPOS_VER\s*=\s*(\d+)",
                    RegexOptions.IgnoreCase
                );

                if (!match.Success)
                {
                    Console.WriteLine($"[POS] JPOS_VER not found on {ip}");
                    return null;
                }

                var raw = match.Groups[1].Value;
                Console.WriteLine($"[POS] JPOS_VER found on {ip}: {raw}");

                return FormatVersion(raw);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[POS] ERROR on {ip}: {ex.Message}");
                return null;
            }
        }

        private static string FormatVersion(string raw)
        {
            if (raw.Length == 3)
                return $"Genius {raw[0]}.{raw[1]}.{raw[2]}";

            if (raw.Length == 4)
                return $"Genius {raw[0]}.{raw[1]}.{raw[2]}.{raw[3]}";

            return $"Genius {raw}";
        }
    }
}
