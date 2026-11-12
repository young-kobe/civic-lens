using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>One page of the executive action list. <see cref="NextCursor"/> is null on the last page.</summary>
public sealed record ExecutiveActionListPageData(int Release, IReadOnlyList<ExecutiveActionListRowData> Actions, RecordListFilter Filter, string? NextCursor,
    IReadOnlyList<SearchChoiceData> Kinds);

/// <summary><see cref="Label"/> is the label column of the view, or null.</summary>
public sealed record ExecutiveActionListRowData(string RecordId, string Headline, string? Label, PublishedExecutiveActionKind Kind, DateOnly SignedOn);
