using System.Reflection;
using CivicLens.Application.Publication;

namespace CivicLens.Host.Publication;

internal static class PublicAssets
{
    public static readonly IReadOnlyList<string> Stylesheets =
        ["assets/theme.css", "assets/preview.css", "assets/diff.css", "assets/public.css"];

    public const string Script = "assets/diff.js";

    public static IReadOnlyList<ReleaseAsset> Load() =>
        [.. Stylesheets.Append(Script).Select(path => new ReleaseAsset(path, Read(path)))];

    private static byte[] Read(string path)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(path)
            ?? throw new InvalidOperationException($"Public asset {path} is not embedded.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
