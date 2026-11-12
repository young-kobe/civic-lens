namespace CivicLens.PublicWeb.Publication;

/// <summary>One page of the statement list. <see cref="OfficialName"/> is the name of the official that the official filter names, taken from a row, or null.
/// <see cref="NextCursor"/> is null on the last page.</summary>
public sealed record StatementListPageData(int Release, IReadOnlyList<StatementListRowData> Statements, RecordListFilter Filter, string? NextCursor,
    IReadOnlyList<SearchChoiceData> Issues, string? OfficialName);

/// <summary><see cref="Label"/> is the label column of the view, or null. <see cref="StatementDate"/> is null when the statement states no date.</summary>
public sealed record StatementListRowData(string RecordId, string Headline, string? Label, DateOnly? StatementDate, IReadOnlyList<string> Speakers);
