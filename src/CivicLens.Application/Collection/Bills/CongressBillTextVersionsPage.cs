namespace CivicLens.Application.Collection.Bills;

/// <summary>The versions of a bill that have a Formatted Text entry.</summary>
public sealed record CongressBillTextVersionsPage(IReadOnlyList<CongressBillTextVersion> Versions)
{
    private static readonly string[] LawTypes = ["Public Law", "Private Law"];
    private const string EnrolledBill = "Enrolled Bill";

    public CongressBillTextVersion? Newest
    {
        get
        {
            var newest = Versions.MaxBy(version => version.Date ?? DateTimeOffset.MinValue);
            if (newest is not null && LawTypes.Contains(newest.Type)) return newest;
            return Versions.FirstOrDefault(version => version.Date is null && version.Type == EnrolledBill) ?? newest;
        }
    }
}
