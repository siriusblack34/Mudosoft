using System.Text.RegularExpressions;

namespace MudoSoft.Backend.Services
{
    public interface IPosInfoService
    {
        Task<string?> GetPosVersion(string ip);
    }

    public class PosInfoService : IPosInfoService
    {
        public async Task<string?> GetPosVersion(string ip)
        {
            try
            {
                string path = $@"\\{ip}\C$\GeniusPOS\parameters.bat";
                if (!File.Exists(path))
                    return null;

                string text = await File.ReadAllTextAsync(path);

                // Örn: set FASHION_JAR=fashion2701_751.jar
                var match = Regex.Match(text, @"FASHION_JAR\s*=\s*(.+\.jar)", RegexOptions.IgnoreCase);
                if (match.Success)
                    return match.Groups[1].Value.Trim();

                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}
