namespace CivicLens.PublicWeb.Publication;

/// <summary>Reads the shared files of <see cref="PublicAssets"/> that this assembly embeds. The release build and the server pages both read them here.</summary>
public static class EmbeddedPublicAssets
{
    public static byte[] Read(string path)
    {
        using var stream = typeof(PublicAssets).Assembly.GetManifestResourceStream(path)
            ?? throw new InvalidOperationException($"Public asset {path} is not embedded.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
