namespace CivicLens.Application.Collection.Bills;

public static class CongressBillTextVersionsParser
{
    private const string FormattedText = "Formatted Text";
    private const string FormattedTextExtension = ".htm";

    public static CongressBillTextVersionsPage Parse(ReadOnlySpan<byte> json, CongressBillListItem expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var wire = CongressApiJson.Deserialize<WirePage>(json, "Text versions page");
        if (wire.TextVersions is null)
            throw new InvalidDataException("Text versions page must contain text versions.");
        if (!string.IsNullOrEmpty(wire.Pagination?.Next))
            throw new InvalidDataException("Text versions page holds more versions than the request limit.");
        var versions = wire.TextVersions.Select(version => ToVersion(version, expected)).OfType<CongressBillTextVersion>();
        return new CongressBillTextVersionsPage([.. versions]);
    }

    private static CongressBillTextVersion? ToVersion(WireVersion? version, CongressBillListItem expected)
    {
        if (version?.Type is not { } type || string.IsNullOrWhiteSpace(type) ||
            type.Trim().Length > CongressBillTextVersion.MaximumTypeLength)
            throw new InvalidDataException("Text version is incomplete.");
        var formatted = version.Formats?.FirstOrDefault(format => format?.Type == FormattedText);
        if (formatted is null) return null;
        return new CongressBillTextVersion(ReadPackageId(formatted.Url, expected), type.Trim(), version.Date, formatted.Url!);
    }

    private static string ReadPackageId(string? url, CongressBillListItem expected)
    {
        if (url is null || url.Length > CongressBillTextVersion.MaximumUrlLength ||
            !url.StartsWith(CongressBillTextVersion.UrlPrefix, StringComparison.Ordinal) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !uri.AbsolutePath.EndsWith(FormattedTextExtension, StringComparison.Ordinal))
            throw new InvalidDataException("Formatted Text entry has no usable URL.");
        var packageId = uri.Segments[^1][..^FormattedTextExtension.Length];
        return BillTextUrl.IsPackageOf(packageId, expected)
            ? packageId
            : throw new InvalidDataException("Formatted Text file name is not a package of the bill.");
    }

    private sealed record WirePage(WireVersion?[]? TextVersions, WirePagination? Pagination);
    private sealed record WireVersion(string? Type, DateTimeOffset? Date, WireFormat?[]? Formats);
    private sealed record WireFormat(string? Type, string? Url);
    private sealed record WirePagination(string? Next);
}
