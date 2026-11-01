namespace CivicLens.Application.Collection.Bills;

/// <summary>The versions of a bill that have a Formatted Text entry.</summary>
public sealed record CongressBillTextVersionsPage(IReadOnlyList<CongressBillTextVersion> Versions)
{
    private const string PublicLaw = "Public Law";

    public CongressBillTextVersion? Newest
    {
        get
        {
            var newestDated = Versions.Where(version => version.Date is not null).MaxBy(version => version.Date);
            if (newestDated?.Type == PublicLaw) return newestDated;
            return Versions.FirstOrDefault(version => version.Date is null) ?? newestDated;
        }
    }
}
