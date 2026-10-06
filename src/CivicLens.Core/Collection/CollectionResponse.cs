using System.Collections.Immutable;

namespace CivicLens.Core.Collection;

/// <summary>Response metadata from this attempt. Encodings retain their original order.</summary>
public sealed class CollectionResponse : IEquatable<CollectionResponse>
{
    private readonly ImmutableArray<string> encodings;

    public CollectionResponse(int statusCode, string? etag, DateTimeOffset? lastModified, string? contentType,
        IEnumerable<string> contentEncodings)
    {
        if (statusCode is < 100 or > 599)
            throw new ArgumentOutOfRangeException(nameof(statusCode));
        ArgumentNullException.ThrowIfNull(contentEncodings);
        if (contentEncodings is ImmutableArray<string> immutableEncodings && immutableEncodings.IsDefault)
            throw new ArgumentException("Content encodings cannot be a default immutable array.", nameof(contentEncodings));

        var snapshot = contentEncodings.ToArray();
        if (snapshot.Any(value => value is null))
            throw new ArgumentException("Content encodings cannot contain null values.", nameof(contentEncodings));

        StatusCode = statusCode;
        ETag = etag;
        LastModified = lastModified;
        ContentType = contentType;
        encodings = ImmutableArray.CreateRange(snapshot);
    }

    public int StatusCode { get; }
    public string? ETag { get; }
    public DateTimeOffset? LastModified { get; }
    public string? ContentType { get; }
    public ImmutableArray<string> ContentEncodings => encodings;

    public bool Equals(CollectionResponse? other) => other is not null &&
        StatusCode == other.StatusCode && ETag == other.ETag && LastModified == other.LastModified &&
        ContentType == other.ContentType && encodings.SequenceEqual(other.encodings, StringComparer.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as CollectionResponse);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(StatusCode);
        hash.Add(ETag, StringComparer.Ordinal);
        hash.Add(LastModified);
        hash.Add(ContentType, StringComparer.Ordinal);
        foreach (var encoding in encodings)
            hash.Add(encoding, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
}
