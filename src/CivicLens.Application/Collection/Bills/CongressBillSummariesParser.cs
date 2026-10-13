using System.Text.Json;
using System.Text.Json.Serialization;
using CivicLens.Application.Documents;
using CivicLens.Collection.Contracts;

namespace CivicLens.Application.Collection.Bills;

public static class CongressBillSummariesParser
{
    public const string Version = "congress-bill-summaries-v1";
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public static string ExtractionVersion(IDocumentTextExtractor extractor)
    {
        ArgumentNullException.ThrowIfNull(extractor);
        return $"{Version}+{extractor.ParserVersion}+{extractor.NormalizationVersion}";
    }

    public static CongressBillSummariesPage Parse(ReadOnlySpan<byte> json, CollectionRequest scope, string sourceUrl,
        CongressBillListItem expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var wire = Deserialize(json);
        if (wire.Summaries is null || wire.Pagination is null)
            throw new InvalidDataException("Summary page must contain summaries and pagination.");
        if (wire.Summaries.Length > BillPageQuery.SummariesPageLimit)
            throw new InvalidDataException("Summary page holds more summaries than the request limit.");
        var summaries = wire.Summaries.Select(ToSummary).ToArray();
        var next = BillPageUrl.ReadNext(wire.Pagination.Next, scope, sourceUrl, ApiResource.BillSummaries, expected);
        return new CongressBillSummariesPage(summaries, next);
    }

    private static WirePage Deserialize(ReadOnlySpan<byte> json)
    {
        try
        {
            return JsonSerializer.Deserialize<WirePage>(json, Options)
                ?? throw new InvalidDataException("Summary page is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Summary page is not valid JSON of the expected shape.", exception);
        }
    }

    private static CongressBillSummary ToSummary(WireSummary? summary)
    {
        if (summary is not { VersionCode: not null, ActionDate: not null, ActionDesc: not null, UpdateDate: not null, Text: not null } ||
            string.IsNullOrWhiteSpace(summary.VersionCode) || string.IsNullOrWhiteSpace(summary.ActionDesc))
            throw new InvalidDataException("Summary is incomplete.");
        return new CongressBillSummary(summary.VersionCode.Trim(), summary.ActionDate.Value, summary.ActionDesc,
            CongressBillParser.ReadUpdateDate(summary.UpdateDate), summary.Text);
    }

    private sealed record WirePage(WireSummary?[]? Summaries, WirePagination? Pagination);
    private sealed record WireSummary(string? VersionCode, DateOnly? ActionDate, string? ActionDesc,
        string? UpdateDate, string? Text);
    private sealed record WirePagination(string? Next);
}
