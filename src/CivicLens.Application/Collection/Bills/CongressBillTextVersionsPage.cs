namespace CivicLens.Application.Collection.Bills;

/// <summary>The versions of a bill that have a Formatted Text entry.</summary>
public sealed record CongressBillTextVersionsPage(IReadOnlyList<CongressBillTextVersion> Versions)
{
    public CongressBillTextVersion? Newest => Versions.MaxBy(version => version.Date ?? DateTimeOffset.MinValue);
}
