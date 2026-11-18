using CivicLens.PublicApi.Bills;
using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Issues;

/// <summary>One bill revision on the issue page, with its headline, cited text version, and published list facts.</summary>
public sealed record IssueBill(string RecordId, int RevisionNumber, string Headline, string? Label, PublishedTextOrigin TextOrigin,
    int Congress, string BillType, int BillNumber, string? BillLine, string? TextVersionName, DateOnly? TextVersionDate)
{
    public BillListFacts? Facts { get; init; }
}
