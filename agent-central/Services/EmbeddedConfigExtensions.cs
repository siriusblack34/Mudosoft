using System.Reflection;

namespace Microsoft.Extensions.Configuration;

public static class EmbeddedConfigExtensions
{
    /// <summary>
    /// Derlenmiş exe içine gömülü appsettings.json'u yükler.
    /// </summary>
    public static IConfigurationBuilder AddEmbeddedJsonFile(
        this IConfigurationBuilder builder, string resourceName)
    {
        var asm = Assembly.GetExecutingAssembly();
        var names = asm.GetManifestResourceNames();
        var match = names.FirstOrDefault(n => n.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));
        if (match == null) return builder;

        var stream = asm.GetManifestResourceStream(match)!;
        builder.AddJsonStream(stream);
        return builder;
    }
}
