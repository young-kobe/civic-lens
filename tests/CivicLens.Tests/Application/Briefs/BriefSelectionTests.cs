using CivicLens.Application.Briefs;
using CivicLens.Tests.Fixtures;
using static CivicLens.Tests.Fixtures.BriefFixtures;

namespace CivicLens.Tests.Application.Briefs;

public sealed class BriefSelectionTests
{
    [Fact]
    public void ActionsOnTheSameBillFormOneGroupAndEveryOtherRecordStandsAlone()
    {
        var records = new[] { Action("a1", "119 hr 1"), Action("a2", "119 hr 1"), Statement("s1"), Statement("s2") };

        var sent = BriefSelection.Select(records, _ => true).Sent;

        Assert.Equal(["a1", "a2"], sent.Take(2).Select(item => item.Record.RecordId).Order());
        Assert.Equal(["R1", "R2", "R3", "R4"], sent.Select(item => item.CitationId));
        Assert.All(sent.Take(2), item => Assert.Equal("119 hr 1", item.Record.Bill));
    }

    [Fact]
    public void BillGroupsDoNotMergeAcrossDifferentBills()
    {
        var records = new[] { Action("a1", "119 hr 1"), Action("a2", "119 hr 2") };

        var sent = BriefSelection.Select(records, _ => true).Sent;

        Assert.Equal(["a1", "a2"], sent.Select(item => item.Record.RecordId));
    }

    [Fact]
    public void GroupsRankByRecordCountThenDistinctOfficialsThenBillMovementThenKey()
    {
        var records = new[]
        {
            Statement("z-single-two-officials", "S", 1, "warren", "markey"),
            Statement("y-single-one-official", "S", 1, "warren"),
            Action("bill-b", "119 hr 3", sponsorships: 3, "warren"),
            Action("bill-a", "119 hr 2", sponsorships: 3, "warren"),
            Action("bill-c", "119 hr 4", sponsorships: 5, "warren"),
            Action("pair-1", "119 hr 9", sponsorships: 1, "warren"),
            Action("pair-2", "119 hr 9", sponsorships: 1, "markey")
        };

        var order = BriefSelection.Select(records, _ => true).Sent.Select(item => item.Record.RecordId);

        Assert.Equal(["pair-1", "pair-2", "z-single-two-officials", "bill-c", "bill-a", "bill-b", "y-single-one-official"], order);
    }

    [Fact]
    public void TheOrderDoesNotDependOnTheOrderOfTheInput()
    {
        var records = new[] { Statement("s1"), Statement("s2"), DocumentChange("d1"), Action("a1") };

        var forward = BriefSelection.Select(records, _ => true).Sent.Select(item => (item.CitationId, item.Record.RecordId));
        var backward = BriefSelection.Select([.. records.Reverse()], _ => true).Sent.Select(item => (item.CitationId, item.Record.RecordId));

        Assert.Equal(forward, backward);
    }

    [Fact]
    public void AGroupEntersWholeOrNotAtAllAndFillingStopsAtTheFirstGroupThatDoesNotFit()
    {
        var records = new[] { Action("a1", "119 hr 1"), Action("a2", "119 hr 1"), Action("b1", "119 hr 2"), Action("b2", "119 hr 2"), Statement("s1") };

        var selection = BriefSelection.Select(records, sent => sent.Length <= 3);

        Assert.Equal(["a1", "a2"], selection.Sent.Select(item => item.Record.RecordId));
        Assert.Equal(5, selection.Considered);
    }

    [Fact]
    public void AGroupThatCannotFitAloneLeavesNothingSoTheStepCanBlockOnTheByteCap()
    {
        var records = new[] { Action("a1", "119 hr 1"), Action("a2", "119 hr 1"), Statement("s1") };

        var selection = BriefSelection.Select(records, sent => sent.Length <= 1);

        Assert.Empty(selection.Sent);
        Assert.Equal(3, selection.Considered);
    }

    [Fact]
    public void CitationIdsAreAssignedAfterTheCutInOrderSoTheyNeverHaveAGap()
    {
        var records = new[] { Statement("s1"), Statement("s2"), Statement("s3") };

        var selection = BriefSelection.Select(records, sent => sent.Length <= 2);

        Assert.Equal(["R1", "R2"], selection.Sent.Select(item => item.CitationId));
    }

    [Fact]
    public void TheFingerprintIgnoresInputOrderAndChangesWithTheRevisionOrTheCutoff()
    {
        var records = new[] { Statement("s1"), DocumentChange("d1") };
        var reordered = new[] { records[1], records[0] };
        var revised = new[] { records[0], records[1] with { RevisionNumber = 2 } };

        var baseline = BriefFingerprint.Compute(3, SelectAll(records).Sent);

        Assert.Matches("^[0-9a-f]{64}$", baseline);
        Assert.Equal(baseline, BriefFingerprint.Compute(3, SelectAll(reordered).Sent));
        Assert.NotEqual(baseline, BriefFingerprint.Compute(3, SelectAll(revised).Sent));
        Assert.NotEqual(baseline, BriefFingerprint.Compute(4, SelectAll(records).Sent));
        Assert.NotEqual(baseline, BriefFingerprint.Compute(3, SelectAll([records[0]]).Sent));
    }

    [Fact]
    public void TheFingerprintTextIsPinnedToItsVersionSoAChangeMustBumpTheVersionPrefix()
    {
        var sent = SelectAll([Statement(new string('1', 64)), DocumentChange(new string('d', 32), "A change", 2)]).Sent;

        var pinned = "96f37304d59d74b11f234d988be05abc411f3190fd93c414e5ec01a13bc9b365";

        Assert.True(BriefFingerprint.Compute(3, sent) == pinned,
            "The fingerprint text changed. Bump the version prefix in BriefFingerprint (brief-fingerprint-v1), and then update the pinned value in this test.");
    }
}
