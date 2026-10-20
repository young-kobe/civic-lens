using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Ingest;

/// <summary>A validated record, reduced to its manifest entry and the two bounded texts that search weighs below the headline.</summary>
public sealed record IndexedRecord(PublishedRecordEntry Entry, string Summary, string Source)
{
    public const int SummaryLimit = 50_000;
    public const int SourceLimit = PublicationProtocol.SearchSourceTextCharacters;

    public string Body => Summary.Length == 0 ? Source : Summary + "\n" + Source;
}
