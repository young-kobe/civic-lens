using CivicLens.Application.Publication;
using CivicLens.PublicWeb.Publication;

namespace CivicLens.Host.Publication;

internal static class PublicAssetFiles
{
    public static IReadOnlyList<ReleaseAsset> Load() => [.. PublicAssets.Paths.Select(path => new ReleaseAsset(path, Read(path)))];

    private static byte[] Read(string path)
    {
        using var stream = typeof(PublicAssets).Assembly.GetManifestResourceStream(path)
            ?? throw new InvalidOperationException($"Public asset {path} is not embedded.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
