using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>One page of the executive action list. <see cref="ActionsInRelease"/> counts every executive action of the release, and a filter does not change it.
/// <see cref="Type"/> is the type filter, or null. <see cref="NextCursor"/> is null on the last page.</summary>
public sealed record ExecutiveActionListPageData(int Release, int ActionsInRelease, IReadOnlyList<ExecutiveActionListRowData> Actions, RecordListFilter Filter,
    PublishedExecutiveActionKind? Type, string? NextCursor, IReadOnlyList<SearchChoiceData> Kinds);

/// <summary>The view has no label column yet, so a row shows no label.</summary>
public sealed record ExecutiveActionListRowData(string RecordId, string Headline, PublishedExecutiveActionKind Kind, DateOnly SignedOn);
