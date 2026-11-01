namespace CivicLens.Application.Collection.Bills;

/// <summary>The versions of a bill that have a Formatted Text entry.</summary>
public sealed record CongressBillTextVersionsPage(IReadOnlyList<CongressBillTextVersion> Versions)
{
    private const string PublicLaw = "Public Law";
    private const string EnrolledBill = "Enrolled Bill";

    public CongressBillTextVersion? Newest
    {
        get
        {
            var newest = Versions.MaxBy(version => version.Date ?? DateTimeOffset.MinValue);
            if (newest?.Type == PublicLaw) return newest;
            return Versions.FirstOrDefault(version => version.Date is null && version.Type == EnrolledBill) ?? newest;
        }
    }
}
