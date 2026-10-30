namespace CivicLens.Application.Collection.Bills;

public sealed record CongressBill(
    int Congress,
    string Type,
    int Number,
    string Title,
    DateOnly IntroducedDate,
    DateTimeOffset UpdateDate,
    string? PolicyArea,
    IReadOnlyList<string> SponsorBioguideIds,
    int CosponsorCount,
    int ActionCount,
    int SummaryCount,
    int SubjectCount)
{
    public static bool HasLegislativeSubjects(int subjectCount, string? policyArea) => subjectCount > (policyArea is null ? 0 : 1);
}
