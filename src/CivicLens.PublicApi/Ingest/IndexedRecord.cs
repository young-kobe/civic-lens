using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Ingest;

/// <summary>
/// A validated record, reduced to its manifest entry and the two bounded texts that search weighs below the headline.
/// <c>SnippetText</c> is the approved summary text that snippets come from. It is null when the record has none.
/// <c>TextOrigin</c> and <c>Label</c> are the text origin and label column of the view that holds the summary, so a search row shows the label of the lists.
/// </summary>
public sealed record IndexedRecord(PublishedRecordEntry Entry, string Summary, string Source, string? SnippetText = null)
{
    public PublishedTextOrigin? TextOrigin { get; init; }
    public string? Label { get; init; }

    public const int SummaryLimit = 50_000;
    public const int SourceLimit = PublicationProtocol.SearchSourceTextCharacters;
}
