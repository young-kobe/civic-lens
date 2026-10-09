using System.Reflection;
using CivicLens.Application.Publication;

namespace CivicLens.Host.Publication;

/// <summary>The workspace's own stylesheets, scripts, and fonts, copied into every release.</summary>
internal static class PublicAssets
{
    public static readonly IReadOnlyList<string> Stylesheets = ["assets/css/theme.css", "assets/css/app.css"];

    public const string ThemeScript = "assets/js/theme.js";

    public const string Script = "assets/js/app.js";

    private static readonly IReadOnlyList<string> Fonts = ["assets/fonts/instrument-sans.woff2", "assets/fonts/newsreader.woff2"];

    public static IReadOnlyList<ReleaseAsset> Load() =>
        [.. Stylesheets.Append(ThemeScript).Append(Script).Concat(Fonts).Select(path => new ReleaseAsset(path, Read(path)))];

    private static byte[] Read(string path)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(path)
            ?? throw new InvalidOperationException($"Public asset {path} is not embedded.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
