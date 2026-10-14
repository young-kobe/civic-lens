using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Issues;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Collection.Bills;

public sealed class BillGateTests
{
    private static readonly IReadOnlySet<string> Covered = new HashSet<string> { "A000001" };
    private static readonly DateTimeOffset UpdatedAt = new(2025, 3, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ACoveredSponsorPassesBeforeThePolicyAreaIsRead()
    {
        var bill = Bill(sponsors: ["Z000009", "A000001"], policyArea: BillGateFixtures.TrackedPolicyArea, cosponsors: 0);

        Assert.Equal(BillGateOutcome.Passed(BillGateReason.CoveredSponsor), BillGate.AtDetail(bill, Covered, Issues()));
    }

    [Fact]
    public void ATrackedPolicyAreaPassesWithoutAnyCosponsor()
    {
        var bill = Bill(policyArea: BillGateFixtures.TrackedPolicyArea, cosponsors: 0);

        Assert.Equal(BillGateOutcome.Passed(BillGateReason.TrackedPolicyArea), BillGate.AtDetail(bill, Covered, Issues()));
    }

    [Fact]
    public void AnUntrackedBillWithoutCosponsorsIsNotRelevantBecauseNobodyCanStillCoverIt()
    {
        Assert.Equal(BillGateOutcome.NotRelevant(BillGateReason.UntrackedPolicyArea),
            BillGate.AtDetail(Bill(policyArea: BillGateFixtures.UntrackedPolicyArea, cosponsors: 0), Covered, Issues()));
        Assert.Equal(BillGateOutcome.NotRelevant(BillGateReason.NoPolicyArea),
            BillGate.AtDetail(Bill(policyArea: null, cosponsors: 0), Covered, Issues()));
    }

    [Fact]
    public void AnUntrackedBillWithCosponsorsWaitsForTheCosponsorPages()
    {
        Assert.Equal(BillGateOutcome.Pending, BillGate.AtDetail(Bill(policyArea: BillGateFixtures.UntrackedPolicyArea, cosponsors: 1), Covered, Issues()));
        Assert.Equal(BillGateOutcome.Pending, BillGate.AtDetail(Bill(policyArea: null, cosponsors: 4), Covered, Issues()));
    }

    [Theory]
    [InlineData("housing and community development")]
    [InlineData("Housing and Community Development ")]
    [InlineData("Unknown area")]
    public void APolicyAreaNameMustEqualAConfiguredNameExactly(string name)
    {
        Assert.Equal(BillGateReason.UntrackedPolicyArea,
            BillGate.AtDetail(Bill(policyArea: name, cosponsors: 0), Covered, Issues()).Reason);
    }

    [Fact]
    public void AConfiguredPolicyAreaNeedsATrackedIssueThatIsActiveOnTheUtcUpdateDay()
    {
        var onUtcDay = Bill(policyArea: BillGateFixtures.TrackedPolicyArea, cosponsors: 0) with
        {
            UpdateDate = new DateTimeOffset(2025, 3, 2, 22, 0, 0, TimeSpan.FromHours(-5))
        };

        Assert.True(BillGate.AtDetail(onUtcDay, Covered, Issues(startsOn: new DateOnly(2025, 3, 3))).IsPassed);
        Assert.False(BillGate.AtDetail(onUtcDay, Covered, Issues(startsOn: new DateOnly(2025, 3, 4))).IsPassed);
        Assert.False(BillGate.AtDetail(onUtcDay, Covered, Issues(endsBefore: new DateOnly(2025, 3, 3))).IsPassed);
        Assert.True(BillGate.AtDetail(onUtcDay, Covered, Issues(endsBefore: new DateOnly(2025, 3, 4))).IsPassed);
    }

    [Fact]
    public void APolicyAreaThatNoIssueListsIsNotTracked()
    {
        var configuration = Issues() with { Issues = [] };

        Assert.Equal(BillGateReason.UntrackedPolicyArea,
            BillGate.AtDetail(Bill(policyArea: BillGateFixtures.TrackedPolicyArea, cosponsors: 0), Covered, configuration).Reason);
    }

    [Fact]
    public void ACoveredCosponsorPassesAPendingGateOnAnyPage()
    {
        var page = new CongressBillCosponsorsPage([Cosponsor("Z000009"), Cosponsor("A000001")], "next");

        Assert.Equal(BillGateOutcome.Passed(BillGateReason.CoveredCosponsor), BillGate.AtCosponsorPage(BillGateFixtures.UntrackedPolicyArea, page, Covered));
    }

    [Fact]
    public void AWithdrawnCosponsorDoesNotCount()
    {
        var withdrawn = Cosponsor("A000001") with { SponsorshipWithdrawnDate = new DateOnly(2025, 4, 1) };

        Assert.Equal(BillGateOutcome.NotRelevant(BillGateReason.UntrackedPolicyArea),
            BillGate.AtCosponsorPage(BillGateFixtures.UntrackedPolicyArea, new CongressBillCosponsorsPage([withdrawn], null), Covered));
    }

    [Fact]
    public void OnlyTheLastPageWithoutACoveredCosponsorDecidesNotRelevant()
    {
        Assert.Equal(BillGateOutcome.Pending,
            BillGate.AtCosponsorPage(BillGateFixtures.UntrackedPolicyArea, new CongressBillCosponsorsPage([Cosponsor("Z000009")], "next"), Covered));
        Assert.Equal(BillGateOutcome.NotRelevant(BillGateReason.UntrackedPolicyArea),
            BillGate.AtCosponsorPage(BillGateFixtures.UntrackedPolicyArea, new CongressBillCosponsorsPage([Cosponsor("Z000009")], null), Covered));
        Assert.Equal(BillGateOutcome.NotRelevant(BillGateReason.NoPolicyArea),
            BillGate.AtCosponsorPage(null, new CongressBillCosponsorsPage([], null), Covered));
    }

    private static CongressBill Bill(string[]? sponsors = null, string? policyArea = null, int cosponsors = 0) =>
        new(119, "hr", 1, "A bill", new DateOnly(2025, 1, 3), UpdatedAt, policyArea, sponsors ?? ["Z000009"], cosponsors, 1, 0);

    private static CongressBillCosponsor Cosponsor(string bioguideId) =>
        new(bioguideId, new DateOnly(2025, 1, 4), false, null);

    private static IssueConfiguration Issues(DateOnly? startsOn = null, DateOnly? endsBefore = null) =>
        BillGateFixtures.Issues(startsOn, endsBefore);

    [Fact]
    public void TrackedIssueIdsListEveryIssueThatIsActiveOnTheDayForThePolicyAreaInOrdinalOrder()
    {
        var issues = Issues() with
        {
            Issues =
            [
                .. Issues().Issues,
                new TrackedIssueConfiguration { Id = "affordability", Name = "Affordability", Definition = "d", PolicyAreaIds = ["housing"] },
                new TrackedIssueConfiguration { Id = "taxes", Name = "Taxes", Definition = "d", PolicyAreaIds = ["taxes"] },
                new TrackedIssueConfiguration
                {
                    Id = "ended", Name = "Ended", Definition = "d", PolicyAreaIds = ["housing"], EndsBefore = new DateOnly(2025, 1, 1)
                }
            ]
        };

        Assert.Equal(["affordability", "rent"], BillGate.TrackedIssueIdsFor(BillGateFixtures.TrackedPolicyArea, new DateOnly(2025, 3, 3), issues));
        Assert.Empty(BillGate.TrackedIssueIdsFor("Unknown", new DateOnly(2025, 3, 3), issues));
        Assert.Empty(BillGate.TrackedIssueIdsFor(null, new DateOnly(2025, 3, 3), issues));
    }
}
