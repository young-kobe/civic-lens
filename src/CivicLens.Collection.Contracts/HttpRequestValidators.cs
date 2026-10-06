using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;

namespace CivicLens.Collection.Contracts;

/// <summary>Conditional header values applied to the last attempted content request.</summary>
public sealed record HttpRequestValidators
{
    [JsonRequired]
    public string? ETag { get; init; }
    [JsonRequired]
    public DateTimeOffset? LastModified { get; init; }

    internal void ValidateAgainst(CollectionRequest request)
    {
        var expectedETag = request.ETag is null ? null : EntityTagHeaderValue.Parse(request.ETag).ToString();
        var expectedDate = request.LastModified is null ? (DateTimeOffset?)null :
            DateTimeOffset.ParseExact(request.LastModified.Value.ToUniversalTime().ToString("r", CultureInfo.InvariantCulture),
                "r", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        if ((ETag is null && LastModified is null) || ETag != expectedETag || LastModified != expectedDate ||
            LastModified is { Offset: var offset } && offset != TimeSpan.Zero)
            throw new InvalidDataException("Sent validators must match the conditional headers applied to the request.");
    }
}
