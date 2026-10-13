using System.Text.Json;

namespace CivicLens.Infrastructure.Review;

internal static class ReviewJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static T Deserialize<T>(string json, string kind) => JsonSerializer.Deserialize<T>(json, Options)
        ?? throw new InvalidOperationException($"Stored {kind} is invalid.");

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
}
