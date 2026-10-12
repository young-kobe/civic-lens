using System.Collections.Immutable;

namespace CivicLens.Application.Analysis;

internal sealed record DocumentChangeDraftingOutput(string Headline, string Summary, string? Significance, string Limits, string Institution,
    DocumentChangeDraftingOutputChangeDate? ChangeDate, ImmutableArray<DocumentChangeDraftingOutputSelection> OfficialIds,
    ImmutableArray<DocumentChangeDraftingOutputSelection> IssueIds, ImmutableArray<DocumentChangeDraftingOutputCitation> Citations, double Confidence);

internal sealed record DocumentChangeDraftingOutputChangeDate(string Date, int CitationIndex, double Confidence);

internal sealed record DocumentChangeDraftingOutputSelection(string Id, double Confidence);

internal sealed record DocumentChangeDraftingOutputCitation(string HunkId, string Side, string Quote);
