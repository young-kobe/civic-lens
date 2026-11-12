using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Ingest;

/// <summary>
/// A validated record, reduced to its manifest entry and the two bounded texts that search weighs below the headline.
/// <c>SnippetText</c> is the approved summary text that snippets come from. It is null when the record has none.
/// </summary>
public sealed record IndexedRecord(PublishedRecordEntry Entry, string Summary, string Source, string? SnippetText = null)
{
    public const int SummaryLimit = 50_000;
    public const int SourceLimit = PublicationProtocol.SearchSourceTextCharacters;

    public string Body => Summary.Length == 0 ? Source : Summary + "\n" + Source;
}
