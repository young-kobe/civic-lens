using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>One page of the bill list of the newest Congress of the release. <see cref="Congress"/> is null when the release holds no bill.
/// <see cref="Statuses"/>, <see cref="PolicyAreas"/> and <see cref="Issues"/> fill the filter choices. <see cref="NextCursor"/> is null on the last page.</summary>
public sealed record BillListPageData(int Release, int? Congress, IReadOnlyList<BillListRowData> Bills, RecordListFilter Filter, string? NextCursor,
    IReadOnlyList<SearchChoiceData> Statuses, IReadOnlyList<SearchChoiceData> PolicyAreas, IReadOnlyList<SearchChoiceData> Issues);

/// <summary><see cref="Label"/> is the label column of the view, or null. <see cref="Sponsor"/> is the first sponsor, or null when the release names none.
/// <see cref="EventDate"/> is the day of the latest sponsorship. The latest action is the text and day that Congress.gov last listed, or null when Civic Lens has not read them.
/// <see cref="Status"/> is the stored status of that action, or null when the release has not stored it.</summary>
public sealed record BillListRowData(string RecordId, string Headline, string? Label, PublishedTextOrigin TextOrigin, string BillType, string? Sponsor, DateOnly EventDate,
    string? LatestActionText, DateOnly? LatestActionDate, BillStatusStage? Status);
