using System.Collections.Immutable;

namespace CivicLens.Application.Analysis;

internal sealed record DocumentChangeDraftOutput(string Headline, string Summary, string? Significance, string Limits, string Institution,
    DocumentChangeDraftOutputChangeDate? ChangeDate, ImmutableArray<DocumentChangeDraftOutputSelection> OfficialIds,
    ImmutableArray<DocumentChangeDraftOutputSelection> IssueIds, ImmutableArray<DocumentChangeDraftOutputCitation> Citations, double Confidence);

internal sealed record DocumentChangeDraftOutputChangeDate(string Date, int CitationIndex, double Confidence);

internal sealed record DocumentChangeDraftOutputSelection(string Id, double Confidence);

internal sealed record DocumentChangeDraftOutputCitation(string HunkId, string Side, string Quote);
