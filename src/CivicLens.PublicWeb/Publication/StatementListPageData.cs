using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>One page of the statement list. <see cref="StatementsInRelease"/> counts every statement of the release, and a filter does not change it.
/// <see cref="NextCursor"/> is null on the last page.</summary>
public sealed record StatementListPageData(int Release, int StatementsInRelease, IReadOnlyList<StatementListRowData> Statements, RecordListFilter Filter, string? NextCursor,
    IReadOnlyList<SearchChoiceData> Issues);

/// <summary><see cref="Label"/> is the label column of the view, or null. <see cref="StatementDate"/> is null when the statement states no date.
/// <see cref="SourceSite"/> is the host name of the source page, such as "kelly.senate.gov". <see cref="LinkedToVote"/> is true when an approved link names a vote.</summary>
public sealed record StatementListRowData(string RecordId, string Headline, string? Label, PublishedTextOrigin TextOrigin, DateOnly? StatementDate,
    IReadOnlyList<StatementSpeakerData> Speakers, string SourceSite, bool LinkedToVote);

/// <summary>One speaker of a statement row, with the seat and party that the officials list shows.</summary>
public sealed record StatementSpeakerData(string Name, PublishedOfficeKind? Kind, string? Office, string? State, int? District, string? Party);
