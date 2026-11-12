namespace CivicLens.PublicWeb.Publication;

/// <summary>One page of the bill list. <see cref="PolicyAreas"/> and <see cref="Issues"/> fill the filter choices. <see cref="NextCursor"/> is null on the last page.</summary>
public sealed record BillListPageData(int Release, IReadOnlyList<BillListRowData> Bills, RecordListFilter Filter, string? NextCursor,
    IReadOnlyList<SearchChoiceData> PolicyAreas, IReadOnlyList<SearchChoiceData> Issues);

/// <summary><see cref="Label"/> is the label column of the view, or null. <see cref="Sponsor"/> is the first sponsor, or null when the release names none.
/// <see cref="EventDate"/> is the day of the latest sponsorship. The latest action is the text and day that Congress.gov last listed, or null when Civic Lens has not read them.</summary>
public sealed record BillListRowData(string RecordId, string Headline, string? Label, string BillType, string? Sponsor, DateOnly EventDate,
    string? LatestActionText, DateOnly? LatestActionDate);
