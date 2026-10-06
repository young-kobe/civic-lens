using System.Net.Http.Headers;

namespace CivicLens.Collection.Contracts;

/// <summary>Headers describe the observed HTTP response; they do not identify or verify the stored capture.</summary>
public sealed record HttpResponseMetadata
{
    public required int StatusCode { get; init; }
    public string? ETag { get; init; }
    public DateTimeOffset? LastModified { get; init; }
    /// <summary>The complete media type value, including parameters such as charset.</summary>
    public string? ContentType { get; init; }
    /// <summary>Ordered HTTP content-coding tokens. Captured entity bytes remain opaque and retain these encodings.</summary>
    public required string[] ContentEncodings { get; init; }

    public void Validate()
    {
        if (StatusCode is < 100 or > 599)
            throw new InvalidDataException("HTTP response status is invalid.");
        ValidateETag();
        ValidateContentType();
        ValidateContentEncodings();
    }

    private void ValidateETag()
    {
        if (ETag is not null && (ETag.Length > 4096 || HasControls(ETag) ||
            !EntityTagHeaderValue.TryParse(ETag, out var tag) || tag.Tag == "*"))
            throw new InvalidDataException("HTTP response ETag is invalid.");
    }

    private void ValidateContentType()
    {
        if (ContentType is not null && (ContentType.Length > 4096 || HasControls(ContentType) ||
            !MediaTypeHeaderValue.TryParse(ContentType, out _)))
            throw new InvalidDataException("HTTP response content type is invalid.");
    }

    private void ValidateContentEncodings()
    {
        if (ContentEncodings is null || ContentEncodings.Length > 16 || ContentEncodings.Any(encoding =>
            encoding is null || encoding.Length is 0 or > 64 || encoding.Any(c => c > 0x7f || !IsTokenChar(c))))
            throw new InvalidDataException("HTTP response content encodings are invalid.");
    }

    private static bool HasControls(string value) => value.Any(c => c < 0x20 || c == 0x7f);

    private static bool IsTokenChar(char c) => char.IsAsciiLetterOrDigit(c) ||
        "!#$%&'*+-.^_`|~".Contains(c, StringComparison.Ordinal);
}
