using System.Text.Json;
using CivicLens.Application.Collection;
using CivicLens.Collection.Contracts;

namespace CivicLens.Host.Review;

internal sealed record ReviewCollectionSettings(CollectionConfiguration Configuration, string ArtifactRoot)
{
    public static ReviewCollectionSettings? FromEnvironment()
    {
        var path = Environment.GetEnvironmentVariable("CIVIC_LENS_COLLECTION_CONFIG");
        var root = Environment.GetEnvironmentVariable("CIVIC_LENS_CAPTURE_DIRECTORY");
        if (path is null && root is null) return null;
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root) ||
            !Path.IsPathFullyQualified(path) || !Path.IsPathFullyQualified(root))
            throw new ArgumentException("Collection configuration and capture directory must both be absolute paths.");
        var file = new FileInfo(path);
        if (file.Length > 1_000_000) throw new ArgumentException("Collection configuration exceeds the workspace limit.");
        var configuration = JsonSerializer.Deserialize<CollectionConfiguration>(File.ReadAllText(path), CollectionProtocol.JsonOptions)
            ?? throw new ArgumentException("Collection configuration is required.");
        configuration.Validate();
        return new(configuration, root);
    }
}
