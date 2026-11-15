using CivicLens.PublicApi.Bills;
using CivicLens.Publication.Contracts;
using CivicLens.PublicWeb.Publication;

namespace CivicLens.Tests.PublicApi.Bills;

public sealed class BillStepsTests
{
    private static readonly BillRow HouseBill = Row("hr");
    private static readonly DateOnly Introduced = new(2025, 9, 11);

    private static BillRow Row(string type) => new()
    {
        RecordId = new string('a', 64),
        RevisionNumber = 1,
        Congress = 119,
        BillType = type,
        BillNumber = 7,
        BillTitle = "Title",
        Headline = "Headline",
        Summary = "Summary",
        TextOrigin = CivicLens.Publication.Contracts.PublishedTextOrigin.Code,
        EventDate = new(2025, 9, 11),
        SponsorCount = 1,
        CosponsorCount = 0
    };

    private static BillActionRow Action(int position, string date, string text, string? type = null) =>
        new() { Position = position, ActionDate = DateOnly.Parse(date), Text = text, Type = type };

    [Fact]
    public void ABillAlwaysStartsWithItsIntroductionInTheChamberOfItsTypeAndNamesTheSponsor()
    {
        var sponsor = new BillSponsorData("p1", "Pat One", "Democrat", new(2025, 9, 11));
        Assert.Equal(new("Introduced in the House", "11 Sep 2025, by Pat One") { Chamber = PublishedChamber.House }, Assert.Single(BillSteps.Of(HouseBill, Introduced, sponsor, [])));
        Assert.Equal(new("Introduced in the Senate", "11 Sep 2025") { Chamber = PublishedChamber.Senate }, Assert.Single(BillSteps.Of(Row("sjres"), Introduced, null, [])));
    }

    /// <summary>The bill event date is the latest sponsorship. The introduction step shows the introduced date of the bill record, with or without a tracked sponsor.</summary>
    [Fact]
    public void TheIntroductionStepShowsTheIntroducedDateAndNeverTheLaterLatestSponsorshipDate()
    {
        var bill = HouseBill with { EventDate = new(2026, 7, 9) };
        var sponsor = new BillSponsorData("p1", "Pat One", "Democrat", new(2026, 4, 6));

        Assert.Equal("6 Apr 2026, by Pat One", BillSteps.Of(bill, new(2026, 4, 6), sponsor, [])[0].Detail);
        Assert.Equal("6 Apr 2026", BillSteps.Of(bill, new(2026, 4, 6), null, [])[0].Detail);
    }

    /// <summary>A revision that the backfill has not reached has no introduced date. The step then shows no detail, not another date.</summary>
    [Fact]
    public void TheIntroductionStepShowsNoDetailWhenTheIntroducedDateIsMissing()
    {
        var sponsor = new BillSponsorData("p1", "Pat One", "Democrat", new(2026, 4, 6));

        Assert.Equal("", BillSteps.Of(HouseBill with { EventDate = new(2026, 7, 9) }, null, sponsor, [])[0].Detail);
        Assert.Equal("", BillSteps.Of(HouseBill, null, null, [])[0].Detail);
    }

    [Fact]
    public void EachChamberPassageIsOneStepWithItsFirstDateAndAnyRecordedVote()
    {
        var steps = BillSteps.Of(HouseBill, Introduced, null,
        [
            Action(0, "2025-09-12", "Passed/agreed to in House: On passage Passed by the Yeas and Nays: 262 - 159 (Roll no. 308)."),
            Action(1, "2025-09-13", "Passed/agreed to in House: On motion to suspend the rules and pass Passed by voice vote."),
            Action(2, "2026-08-07", "Passed Senate with an amendment by Unanimous Consent.")
        ]);

        Assert.Equal(["Introduced in the House", "Passed the House", "Passed the Senate"], steps.Select(step => step.Title));
        Assert.Equal([PublishedChamber.House, PublishedChamber.House, PublishedChamber.Senate], steps.Select(step => step.Chamber));
        Assert.Equal("12 Sep 2025, vote of 262 to 159", steps[1].Detail);
        Assert.Equal("7 Aug 2026", steps[2].Detail);
    }

    [Fact]
    public void PresentationToThePresidentAndAVetoAreStepsThatShowTheCongressGovActionDateAndHaveNoChamber()
    {
        var steps = BillSteps.Of(HouseBill, Introduced, null,
        [
            Action(0, "2026-09-17", "Presented to President."),
            Action(1, "2026-09-25", "Vetoed by President.")
        ]);

        Assert.Equal(["Introduced in the House", "Presented to the President", "Vetoed by the President"], steps.Select(step => step.Title));
        Assert.Equal(["17 Sep 2026", "25 Sep 2026"], steps.Skip(1).Select(step => step.Detail));
        Assert.All(steps.Skip(1), step => Assert.Null(step.Chamber));
    }

    [Theory]
    [InlineData("Presented to President. (Text of the message)")]
    [InlineData("The bill was presented to President Smith.")]
    [InlineData("Vetoed by President in part.")]
    [InlineData("Veto message received from the President.")]
    public void ATextThatIsNoWholePresidentShapeAddsNoStep(string text)
    {
        Assert.Single(BillSteps.Of(HouseBill, Introduced, null, [Action(0, "2026-09-17", text)]));
    }

    [Theory]
    [InlineData("Passed/agreed to in House: On motion to recommit Failed by the Yeas and Nays: 200 - 210")]
    [InlineData("Passed/agreed to in House: On agreeing to the Smith amendment Agreed to by voice vote.")]
    [InlineData("Motion to reconsider laid on the table Agreed to without objection.")]
    [InlineData("The House passed the bill earlier.")]
    public void ATextThatIsNoWholeKnownPassageShapeAddsNoStep(string text)
    {
        Assert.Single(BillSteps.Of(HouseBill, Introduced, null, [Action(0, "2025-09-12", text)]));
    }

    [Fact]
    public void AResolvingDifferencesActionNeverCountsAsAPassage()
    {
        var steps = BillSteps.Of(HouseBill, Introduced, null,
            [Action(0, "2026-09-16", "Passed/agreed to in House: On motion that the House agree to the Senate amendments", "ResolvingDifferences")]);

        Assert.Single(steps);
    }

    [Fact]
    public void AgreeingToTheOtherChambersChangesIsAStepWithItsVoteButADisagreementOrAConferenceReportIsNot()
    {
        var steps = BillSteps.Of(HouseBill, Introduced, null,
        [
            Action(0, "2026-09-15", "Resolving differences -- House actions: On motion that the House disagree to the Senate amendments Agreed to by voice vote.", "ResolvingDifferences"),
            Action(1, "2026-09-15", "Resolving differences -- House actions: On agreeing to the conference report Agreed to by the Yeas and Nays: 300 - 100.", "ResolvingDifferences"),
            Action(2, "2026-09-16", "Resolving differences -- House actions: On motion that the House agree to the Senate amendments Agreed to by the Yeas and Nays: (2/3 required): 262 - 159 (Roll no. 308).", "ResolvingDifferences")
        ]);

        var step = Assert.Single(steps.Skip(1));
        Assert.Equal(new("House agreed to the Senate changes", "16 Sep 2026, vote of 262 to 159") { Chamber = PublishedChamber.House }, step);
    }

    [Fact]
    public void BecameLawNeedsTheCompleteOfficialLawTextAndTakesTheLawNumberFromIt()
    {
        var law = Action(1, "2026-09-18", "Became Public Law No: 119-111.", "BecameLaw");
        var steps = BillSteps.Of(HouseBill, Introduced, null, [Action(0, "2026-09-17", "Presented to President."), law]);

        Assert.Equal(new("Became law", "18 Sep 2026, Public Law 119-111"), steps[^1]);
        Assert.Same(law, BillSteps.LawAction([law]));
        Assert.Null(BillSteps.LawAction([Action(0, "2026-09-18", "Became Public Law No: 119-111 after the veto.")]));
        Assert.Equal("Vetoed by the President", BillSteps.Of(HouseBill, Introduced, null, [Action(0, "2026-09-17", "Vetoed by President.")])[^1].Title);
    }

    [Fact]
    public void StepsFollowTheActionDatesEvenWhenTheHistoryListsNewestFirst()
    {
        var steps = BillSteps.Of(HouseBill, Introduced, null,
        [
            Action(0, "2026-08-07", "Passed Senate with an amendment by Unanimous Consent."),
            Action(1, "2025-09-12", "Passed/agreed to in House: On passage Passed by voice vote.")
        ]);

        Assert.Equal(["Introduced in the House", "Passed the House", "Passed the Senate"], steps.Select(step => step.Title));
    }
}
