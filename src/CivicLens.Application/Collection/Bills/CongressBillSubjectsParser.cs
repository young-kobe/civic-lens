using CivicLens.Collection.Contracts;

namespace CivicLens.Application.Collection.Bills;

public static class CongressBillSubjectsParser
{
    public const string ExtractionVersion = "congress-bill-subjects-v1";
    public static CongressBillSubjectsPage Parse(ReadOnlySpan<byte> json, CollectionRequest scope, string sourceUrl,
        CongressBillListItem expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var wire = CongressApiJson.Deserialize<WirePage>(json, "Subject page");
        if (wire.Subjects?.LegislativeSubjects is null || wire.Pagination is null)
            throw new InvalidDataException("Subject page must contain legislative subjects and pagination.");
        if (wire.Subjects.LegislativeSubjects.Length > ApiPageQuery.PageLimit)
            throw new InvalidDataException("Subject page holds more subjects than the request limit.");
        var subjects = wire.Subjects.LegislativeSubjects.Select(ToSubject).ToArray();
        var next = BillPageUrl.ReadNext(wire.Pagination.Next, scope, sourceUrl, ApiResource.BillSubjects, expected);
        return new CongressBillSubjectsPage(subjects, next);
    }

    private static string ToSubject(WireSubject? subject) =>
        string.IsNullOrWhiteSpace(subject?.Name) ? throw new InvalidDataException("Subject is incomplete.") : subject.Name.Trim();

    private sealed record WirePage(WireSubjects? Subjects, WirePagination? Pagination);
    private sealed record WireSubjects(WireSubject?[]? LegislativeSubjects);
    private sealed record WireSubject(string? Name);
    private sealed record WirePagination(string? Next);
}
