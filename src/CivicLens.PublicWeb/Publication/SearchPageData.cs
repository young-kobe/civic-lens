using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>One search results page. The snippet marks each match between U+E000 and U+E001, as the search index gives it.</summary>
public sealed record SearchPageData(SearchQueryData Query, int Release, IReadOnlyList<SearchOfficialData> Officials,
    SearchBillData? Bill, IReadOnlyList<SearchHitData> Hits, string? NextCursor, IReadOnlyList<SearchChoiceData> Issues);

/// <summary>The request as the reader typed it. <see cref="Official"/> is an official ID from an official card link.</summary>
public sealed record SearchQueryData(string? Text, string? Kind, string? Official, string? Issue, string? From, string? To)
{
    public bool IsEmpty => Text is null && Kind is null && Official is null && Issue is null && From is null && To is null;
}

public sealed record SearchOfficialData(string OfficialId, string Name, PublishedOfficeKind? Kind, string? State, int? District, string? Party);

/// <summary>The newest bill whose number is the whole query. <see cref="Others"/> counts further bills with the same number.</summary>
public sealed record SearchBillData(string RecordId, string Line, int Congress, string? PublicLaw, int Others);

public sealed record SearchHitData(PublishedRecordKind Kind, string RecordId, string Headline, DateOnly? Day, string? BillLine,
    string? Snippet, IReadOnlyList<string> Officials);

public sealed record SearchChoiceData(string Value, string Label);
