namespace CivicLens.PublicWeb.Publication;

/// <summary>One page of the document change list. <see cref="Institutions"/> and <see cref="Issues"/> fill the filter choices. <see cref="NextCursor"/> is null on the last page.</summary>
public sealed record DocumentChangeListPageData(int Release, IReadOnlyList<DocumentChangeListRowData> Changes, RecordListFilter Filter, string? NextCursor,
    IReadOnlyList<SearchChoiceData> Institutions, IReadOnlyList<SearchChoiceData> Issues);

/// <summary><see cref="Label"/> is the label column of the view, or null. <see cref="ChangeDate"/> is null when the page states no date. <see cref="PlaceCount"/> counts the places that changed, or is null when the view holds no count.</summary>
public sealed record DocumentChangeListRowData(string RecordId, string Headline, string? Label, string Institution, DateOnly? ChangeDate, int? PlaceCount);
