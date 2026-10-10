using System.Text.Json;
using CivicLens.Application.Collection;
using CivicLens.Collection.Contracts;

namespace CivicLens.Host.Review;

internal sealed record ReviewCollectionSettings(CollectionConfiguration Configuration)
{
    public static ReviewCollectionSettings? FromEnvironment()
    {
        var path = Environment.GetEnvironmentVariable("CIVIC_LENS_COLLECTION_CONFIG");
        if (path is null) return null;
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new ArgumentException("Collection configuration must be an absolute path.");
        var file = new FileInfo(path);
        if (file.Length > 1_000_000) throw new ArgumentException("Collection configuration exceeds the workspace limit.");
        var configuration = JsonSerializer.Deserialize<CollectionConfiguration>(File.ReadAllText(path), CollectionProtocol.JsonOptions)
            ?? throw new ArgumentException("Collection configuration is required.");
        configuration.Validate();
        return new(configuration);
    }
}
