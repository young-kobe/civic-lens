using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>One search results page. The snippet marks each match between U+E000 and U+E001, as the search index gives it.
/// <see cref="FilterOfficialName"/> is the name of the official that the official filter names, taken from a result, or null.</summary>
public sealed record SearchPageData(SearchQueryData Query, int Release, IReadOnlyList<SearchOfficialData> Officials,
    SearchBillData? Bill, IReadOnlyList<SearchHitData> Hits, string? NextCursor, IReadOnlyList<SearchChoiceData> Issues, string? FilterOfficialName);

/// <summary>The request as the reader typed it. <see cref="Official"/> is an official ID from an official card link.</summary>
public sealed record SearchQueryData(string? Text, string? Kind, string? Official, string? Issue, string? From, string? To)
{
    public bool IsEmpty => Text is null && Kind is null && Official is null && Issue is null && From is null && To is null;
}

public sealed record SearchOfficialData(string OfficialId, string Name, PublishedOfficeKind? Kind, string? Office, string? State, int? District, string? Party);

/// <summary>The newest bill whose number is the whole query. <see cref="Sponsor"/> is the name of its first sponsor. <see cref="LawDate"/> is the day it became law,
/// when the latest action says so. <see cref="HouseVoteRecordId"/> is the newest House roll call on it. <see cref="Others"/> are further bills with the same number,
/// and <see cref="MoreOthers"/> says that the list holds more than those.</summary>
public sealed record SearchBillData(string RecordId, string Line, int Congress, string? Sponsor, string? PublicLaw, DateOnly? LawDate, string? HouseVoteRecordId,
    IReadOnlyList<SearchOtherBillData> Others, bool MoreOthers);

/// <summary>Another bill with the number of the query. <see cref="Number"/> is the short bill number, such as S. 5334.</summary>
public sealed record SearchOtherBillData(string RecordId, string Number, PublishedChamber Chamber, string? Sponsor);

public sealed record SearchHitData(PublishedRecordKind Kind, string RecordId, string Headline, DateOnly? Day, string? BillLine,
    string? Snippet, IReadOnlyList<string> Officials);

public sealed record SearchChoiceData(string Value, string Label);
