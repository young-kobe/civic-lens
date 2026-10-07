using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CivicLens.Core.Documents;

public sealed class DocumentContentProfile
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly Regex SelectorPattern = new("\\A[#.]?[A-Za-z_][A-Za-z0-9_-]*\\z",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public DocumentContentProfile(string id, string selector, IEnumerable<string> excludedSelectors)
    {
        ValidateId(id);
        ValidateSelector(selector, nameof(selector));
        ArgumentNullException.ThrowIfNull(excludedSelectors);
        if (excludedSelectors is ImmutableArray<string> { IsDefault: true })
            throw new ArgumentException("Excluded selectors cannot be an uninitialized immutable array.", nameof(excludedSelectors));

        var exclusions = new List<string>();
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (var exclusion in excludedSelectors)
        {
            if (exclusions.Count == 16)
                throw new ArgumentException("A profile can exclude at most 16 selectors.", nameof(excludedSelectors));
            ValidateSelector(exclusion, nameof(excludedSelectors));
            if (!unique.Add(exclusion))
                throw new ArgumentException("Excluded selectors must be unique.", nameof(excludedSelectors));
            exclusions.Add(exclusion);
        }

        Id = id;
        Selector = selector;
        ExcludedSelectors = ImmutableArray.CreateRange(exclusions);
        RevisionId = ComputeRevisionId(id, selector, exclusions);
    }

    public string Id { get; }
    public string Selector { get; }
    public ImmutableArray<string> ExcludedSelectors { get; }
    public string RevisionId { get; }

    public bool Matches(DocumentContentProfile? other) => other is not null &&
        Id == other.Id && Selector == other.Selector && ExcludedSelectors.SequenceEqual(other.ExcludedSelectors, StringComparer.Ordinal);

    private static void ValidateId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 128 || id.Contains('\0'))
            throw new ArgumentException("Profile ID must contain 1 to 128 non-NUL characters.", nameof(id));
        try { _ = StrictUtf8.GetByteCount(id); }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException("Profile ID must contain valid Unicode.", nameof(id), exception);
        }
    }

    private static void ValidateSelector(string selector, string parameterName)
    {
        if (selector is null || selector.Length > 128 || !SelectorPattern.IsMatch(selector))
            throw new ArgumentException("Selector must be one tag name, ID, or class using a simple ASCII identifier.", parameterName);
    }

    private static string ComputeRevisionId(string id, string selector, IReadOnlyCollection<string> exclusions)
    {
        using var buffer = new MemoryStream();
        WriteFramed(buffer, id);
        WriteFramed(buffer, selector);
        Span<byte> count = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(count, exclusions.Count);
        buffer.Write(count);
        foreach (var exclusion in exclusions)
            WriteFramed(buffer, exclusion);
        return Convert.ToHexStringLower(SHA256.HashData(buffer.ToArray()));
    }

    private static void WriteFramed(Stream stream, string value)
    {
        var bytes = StrictUtf8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        stream.Write(length);
        stream.Write(bytes);
    }
}
