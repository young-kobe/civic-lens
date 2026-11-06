using CivicLens.Application.Publication;
using CivicLens.PublicWeb.Publication;

namespace CivicLens.Host.Publication;

internal static class PublicAssetFiles
{
    public static IReadOnlyList<ReleaseAsset> Load() => [.. PublicAssets.Paths.Select(path => new ReleaseAsset(path, EmbeddedPublicAssets.Read(path)))];
}
