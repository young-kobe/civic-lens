using CivicLens.PublicApi.Http;
using CivicLens.PublicWeb.Publication;
using Microsoft.AspNetCore.Http;

namespace CivicLens.PublicApi.Officials;

/// <summary>The query of the record page of one official: the record kind of the tab and where the page starts. A missing kind selects the first kind that has rows.</summary>
public sealed record OfficialRecordRequest(OfficialRecordKind? Kind, OfficialRecordCursor? Cursor)
{
    /// <summary>Reads the query string. An invalid value throws <see cref="ArgumentException"/> that names the parameter.</summary>
    public static OfficialRecordRequest Parse(IQueryCollection query) => new(KindOf(QueryValues.Value(query, "kind")), OfficialRecordCursor.Parse(QueryValues.Value(query, "cursor")));

    private static OfficialRecordKind? KindOf(string? value) => string.IsNullOrEmpty(value)
        ? null
        : Enum.GetValues<OfficialRecordKind>().Cast<OfficialRecordKind?>().FirstOrDefault(kind => OfficialRecordText.Query(kind!.Value) == value)
            ?? throw new ArgumentException($"kind must be {string.Join(", ", Enum.GetValues<OfficialRecordKind>().Select(OfficialRecordText.Query))}.", "kind");
}
