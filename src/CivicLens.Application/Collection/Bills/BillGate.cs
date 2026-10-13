using CivicLens.Application.Issues;
using CivicLens.Core.Registry;

namespace CivicLens.Application.Collection.Bills;

public static class BillGate
{
    public static DateOnly DayOf(DateTimeOffset updateDate) => DateOnly.FromDateTime(updateDate.UtcDateTime);

    public static BillGateOutcome AtDetail(CongressBill bill, IReadOnlySet<string> coveredBioguideIds,
        IssueConfiguration issues)
    {
        ArgumentNullException.ThrowIfNull(bill);
        ArgumentNullException.ThrowIfNull(coveredBioguideIds);
        ArgumentNullException.ThrowIfNull(issues);
        if (bill.SponsorBioguideIds.Any(coveredBioguideIds.Contains))
            return BillGateOutcome.Passed(BillGateReason.CoveredSponsor);
        if (IsTracked(bill.PolicyArea, DayOf(bill.UpdateDate), issues))
            return BillGateOutcome.Passed(BillGateReason.TrackedPolicyArea);
        return bill.CosponsorCount == 0
            ? BillGateOutcome.NotRelevant(UntrackedReason(bill.PolicyArea))
            : BillGateOutcome.Pending;
    }

    public static BillGateOutcome AtCosponsorPage(string? policyArea, CongressBillCosponsorsPage page,
        IReadOnlySet<string> coveredBioguideIds)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(coveredBioguideIds);
        if (page.Cosponsors.Any(cosponsor => IsActiveCoveredCosponsor(cosponsor, coveredBioguideIds)))
            return BillGateOutcome.Passed(BillGateReason.CoveredCosponsor);
        return page.Next is null ? BillGateOutcome.NotRelevant(UntrackedReason(policyArea)) : BillGateOutcome.Pending;
    }

    private static bool IsActiveCoveredCosponsor(CongressBillCosponsor cosponsor, IReadOnlySet<string> covered) =>
        cosponsor.SponsorshipWithdrawnDate is null && covered.Contains(cosponsor.BioguideId);

    private static BillGateReason UntrackedReason(string? policyArea) =>
        policyArea is null ? BillGateReason.NoPolicyArea : BillGateReason.UntrackedPolicyArea;

    private static bool IsTracked(string? policyArea, DateOnly day, IssueConfiguration issues)
    {
        var area = issues.PolicyAreas.SingleOrDefault(candidate => string.Equals(candidate.Name, policyArea, StringComparison.Ordinal));
        return area is not null && issues.Issues.Any(issue => issue.PolicyAreaIds.Contains(area.Id) && IsActiveOn(issue, day));
    }

    private static bool IsActiveOn(TrackedIssueConfiguration issue, DateOnly day) =>
        new DateRange(issue.StartsOn, issue.EndsBefore).Contains(day);
}
