using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>One search results page. The snippet marks each match between U+E000 and U+E001, as the search index gives it.
/// <see cref="FilterOfficialName"/> is the name of the official that the official filter names, taken from a result, or null.
/// <see cref="MoreOfficials"/> says that more officials match the text than the page shows.</summary>
public sealed record SearchPageData(SearchQueryData Query, int Release, IReadOnlyList<SearchOfficialData> Officials, bool MoreOfficials,
    SearchBillData? Bill, IReadOnlyList<SearchHitData> Hits, string? NextCursor, IReadOnlyList<SearchChoiceData> Issues, string? FilterOfficialName);

/// <summary>The request as the reader typed it. <see cref="Official"/> is an official ID from an official card link.</summary>
public sealed record SearchQueryData(string? Text, string? Kind, string? Official, string? Issue, string? From, string? To)
{
    /// <summary>The accepted parameters that the form has no field for. The search API accepts them, so the next page link carries each one,
    /// and a later page keeps the same filters, sort, and page size.</summary>
    public string? PolicyArea { get; init; }
    public string? Source { get; init; }
    public string? Sort { get; init; }
    public string? Direction { get; init; }
    public int? Limit { get; init; }

    public bool IsEmpty => Text is null && !HasFilter;

    /// <summary>True when a kind, official, issue, policy area, source, or date filter narrows the results.</summary>
    public bool HasFilter => Kind is not null || Official is not null || Issue is not null || PolicyArea is not null || Source is not null || From is not null || To is not null;
}

public sealed record SearchOfficialData(string OfficialId, string Name, PublishedOfficeKind? Kind, string? Office, string? State, int? District, string? Party);

/// <summary>The newest bill whose number is the whole query. <see cref="Sponsor"/> is the name of its first sponsor. <see cref="LawDate"/> is the day it became law,
/// when the latest action says so. <see cref="Vote"/> is the newest House roll call on it, or the newest Senate roll call when it has no House roll call.
/// <see cref="Others"/> are further bills with the same number whose records are results on this page.</summary>
public sealed record SearchBillData(string RecordId, string Line, int Congress, string? Sponsor, string? PublicLaw, DateOnly? LawDate, SearchBillVoteData? Vote,
    IReadOnlyList<SearchOtherBillData> Others);

/// <summary>One roll call vote on the matched bill and the chamber that held it.</summary>
public sealed record SearchBillVoteData(string RecordId, PublishedChamber Chamber);

/// <summary>Another bill with the number of the query. <see cref="Number"/> is the short bill number, such as S. 5334.</summary>
public sealed record SearchOtherBillData(string RecordId, string Number, PublishedChamber Chamber, string? Sponsor);

public sealed record SearchHitData(PublishedRecordKind Kind, string RecordId, string Headline, DateOnly? Day, string? BillLine,
    string? Snippet, IReadOnlyList<string> Officials);

public sealed record SearchChoiceData(string Value, string Label);
