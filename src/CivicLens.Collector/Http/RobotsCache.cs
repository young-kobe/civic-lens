using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CivicLens.Collector.Http;

internal sealed record CachedRobots(string Text, DateTimeOffset FetchedAt, bool NotFound = false);

internal static class RobotsCache
{
    public static readonly TimeSpan FreshFor = TimeSpan.FromHours(24);
    public static readonly TimeSpan StaleIfErrorFor = TimeSpan.FromDays(7);
    private const long MaximumFileBytes = 10_000_000 + 4096;

    public static async Task<CachedRobots?> ReadAsync(string artifactDirectory, string allowedOrigin,
        CancellationToken cancellationToken)
    {
        var path = PathOf(artifactDirectory, allowedOrigin);
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.LinkTarget is not null || info.Length > MaximumFileBytes) return null;
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<CachedRobots>(stream, cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static async Task TryWriteAsync(string artifactDirectory, string allowedOrigin, CachedRobots robots,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetFullPath(artifactDirectory);
        var temp = Path.Combine(directory, ".robots-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllBytesAsync(temp, JsonSerializer.SerializeToUtf8Bytes(robots), cancellationToken);
            File.Move(temp, PathOf(artifactDirectory, allowedOrigin), overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        finally { TryDelete(temp); }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private static string PathOf(string artifactDirectory, string allowedOrigin)
    {
        var origin = new Uri(allowedOrigin);
        var key = $"{origin.Scheme.ToLowerInvariant()}://{origin.IdnHost.ToLowerInvariant()}:{origin.Port}";
        var name = "robots-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key))) + ".json";
        return Path.Combine(Path.GetFullPath(artifactDirectory), name);
    }
}
