using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>One search results page. The snippet marks each match between U+E000 and U+E001, as the search index gives it.
/// <see cref="FilterOfficialName"/> is the name of the official that the official filter names, taken from a result, or null.
/// <see cref="MoreOfficials"/> says that more officials match the text than the page shows. <see cref="Total"/> counts every record of the release that the request matches.</summary>
public sealed record SearchPageData(SearchQueryData Query, int Release, IReadOnlyList<SearchOfficialData> Officials, bool MoreOfficials,
    SearchBillData? Bill, IReadOnlyList<SearchHitData> Hits, int Total, string? NextCursor, IReadOnlyList<SearchChoiceData> Issues, string? FilterOfficialName)
{
    /// <summary>The cursor of the previous page, or null on the first page.</summary>
    public string? PreviousCursor { get; init; }
}

/// <summary>The request as the reader typed it. <see cref="Official"/> is an official ID from an official card link.</summary>
public sealed record SearchQueryData(string? Text, string? Kind, string? Official, string? Issue, string? From, string? To)
{
    /// <summary>The field the server refused, or null when the query was accepted.</summary>
    public string? Invalid { get; init; }

    /// <summary>The accepted parameters that have no visible form control. Filter forms and page links carry each one,
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

/// <summary>One search result. <see cref="Label"/> and <see cref="TextOrigin"/> are the label and text origin columns of the view that the index copied.</summary>
public sealed record SearchHitData(PublishedRecordKind Kind, string RecordId, string Headline, DateOnly? Day, string? BillLine,
    string? Snippet, IReadOnlyList<string> Officials)
{
    public string? Label { get; init; }
    public PublishedTextOrigin? TextOrigin { get; init; }
}

public sealed record SearchChoiceData(string Value, string Label);
