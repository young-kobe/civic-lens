using System.Collections.Immutable;

namespace CivicLens.Application.Analysis;

internal sealed record DocumentChangeDraftingOutput(string Headline, string Summary, string? Significance, string Limits, string Institution,
    DocumentChangeDraftingOutputChangeDate? ChangeDate, ImmutableArray<string> OfficialIds, ImmutableArray<string> IssueIds,
    ImmutableArray<DocumentChangeDraftingOutputCitation> Citations);

internal sealed record DocumentChangeDraftingOutputChangeDate(string Date, int CitationIndex);

internal sealed record DocumentChangeDraftingOutputCitation(string HunkId, string Side, string Quote);
