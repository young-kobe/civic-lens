using CivicLens.PublicApi.Http;
using CivicLens.PublicWeb.Publication;
using Microsoft.AspNetCore.Http;

namespace CivicLens.PublicApi.Officials;

/// <summary>The query of the record page of one official: the record kind of the tab, where the page starts, and the filters of the votes tab.
/// A missing kind selects the first kind that has rows. A vote filter selects the votes tab, and no other tab takes one.</summary>
public sealed record OfficialRecordRequest(OfficialRecordKind? Kind, OfficialRecordCursor? Cursor)
{
    /// <summary>The query names of the vote filters, in form order. The values of <see cref="Filter"/> carry them to the next page link.</summary>
    public static readonly string[] FilterNames = ["issue", "bill", "party", "cast"];

    public const string AgainstParty = "against";
    public const string Yes = "yes";
    public const string No = "no";

    public string? Issue { get; init; }
    /// <summary>The lower case form, or null when the reader typed a bare number.</summary>
    public string? BillType { get; init; }
    public int? BillNumber { get; init; }
    /// <summary>Only the votes where the official voted against the majority of their party, the column <c>member_votes.breaks_party</c>.</summary>
    public bool AgainstPartyOnly { get; init; }
    /// <summary><see cref="Yes"/>, <see cref="No"/>, or null for any cast.</summary>
    public string? Cast { get; init; }

    /// <summary>The filters as the form shows them, in form order. An empty filter keeps no value.</summary>
    public RecordListFilter Filter => RecordListFilter.Of(null, null, ("issue", Issue), ("bill", QueryValues.BillNumberText(BillType, BillNumber)),
        ("party", AgainstPartyOnly ? AgainstParty : null), ("cast", Cast));

    /// <summary>Reads the query string. An invalid value throws <see cref="ArgumentException"/> that names the parameter.</summary>
    public static OfficialRecordRequest Parse(IQueryCollection query)
    {
        var (billType, billNumber) = QueryValues.BillNumber(query, "bill");
        var request = new OfficialRecordRequest(KindOf(QueryValues.Value(query, "kind")), OfficialRecordCursor.Parse(QueryValues.Value(query, "cursor")))
        {
            Issue = QueryValues.Filter(query, "issue"),
            BillType = billType,
            BillNumber = billNumber,
            AgainstPartyOnly = OneOf(query, "party", AgainstParty) is not null,
            Cast = OneOf(query, "cast", Yes, No)
        };
        if (!request.Filter.IsActive) return request;
        return request.Kind is null or OfficialRecordKind.Votes
            ? request with { Kind = OfficialRecordKind.Votes }
            : throw new ArgumentException("Only the votes tab takes a filter.", "kind");
    }

    /// <summary>Reads the query string. A refused query gives the first page of the votes tab and the filter of the refused request: the typed text and the refused field.</summary>
    public static (OfficialRecordRequest Request, RecordListFilter? Refused) ParseOrRefuse(IQueryCollection query)
    {
        try
        {
            return (Parse(query), null);
        }
        catch (ArgumentException refused)
        {
            return (new(OfficialRecordKind.Votes, null), RecordListFilter.Refused(refused.ParamName, QueryValues.Typed(query, FilterNames)));
        }
    }

    private static string? OneOf(IQueryCollection query, string name, params string[] values) =>
        QueryValues.Filter(query, name) is not { } value ? null
        : values.Contains(value, StringComparer.Ordinal) ? value
        : throw new ArgumentException($"{name} must be {string.Join(" or ", values)}.", name);

    private static OfficialRecordKind? KindOf(string? value) => string.IsNullOrEmpty(value)
        ? null
        : Enum.GetValues<OfficialRecordKind>().Cast<OfficialRecordKind?>().FirstOrDefault(kind => OfficialRecordText.Query(kind!.Value) == value)
            ?? throw new ArgumentException($"kind must be {string.Join(", ", Enum.GetValues<OfficialRecordKind>().Select(OfficialRecordText.Query))}.", "kind");
}
