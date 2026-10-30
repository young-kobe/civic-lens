using CivicLens.Host.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Host;

public sealed class PublishedOfficialViewTests
{
    private static readonly DateTimeOffset Release = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TermsThatFollowEachOtherInTheSameSeatMakeOneStretchEvenWhenTheNextTermStartsAfterTheSwearingInGap()
    {
        var stretches = PublishedOfficialView.OfficeStretches(
        [
            House(new(1997, 1, 7), new(1999, 1, 3)), House(new(1999, 1, 6), new(2001, 1, 3)), House(new(2001, 1, 3), new(2003, 1, 3))
        ]);

        var stretch = Assert.Single(stretches);
        Assert.Equal((new DateOnly(1997, 1, 7), new DateOnly(2003, 1, 3), 3), (stretch.Start, stretch.End!.Value, stretch.Terms));
    }

    [Fact]
    public void ADifferentDistrictPartyOrOfficeStartsANewStretchBecauseTheRowNamesOneSeat()
    {
        var stretches = PublishedOfficialView.OfficeStretches(
        [
            House(new(2001, 1, 3), new(2003, 1, 3)), House(new(2003, 1, 3), new(2005, 1, 3)) with { District = 2 },
            House(new(2005, 1, 3), new(2007, 1, 3)) with { District = 2, Party = "Republican" },
            Senate(new(2007, 1, 4), null)
        ]);

        Assert.Equal(4, stretches.Count);
    }

    [Fact]
    public void ABreakBetweenTermsKeepsTwoStretchesBecauseTheOfficialLeftOfficeInBetween()
    {
        var stretches = PublishedOfficialView.OfficeStretches([House(new(2001, 1, 3), new(2003, 1, 3)), House(new(2007, 1, 4), new(2009, 1, 3))]);

        Assert.Equal(2, stretches.Count);
    }

    [Fact]
    public void TheTenureCountsTermsFromTheStartOfTheUnbrokenStretchInTheChamberEvenAfterADistrictChange()
    {
        var tenure = PublishedOfficialView.Tenure(
        [
            House(new(1997, 1, 7), new(1999, 1, 3)), House(new(1999, 1, 6), new(2001, 1, 3)) with { District = 8 }, House(new(2001, 1, 3), new(2027, 1, 3))
        ], Release);

        Assert.Equal("In the House since January 1997, now in the 3rd term.", tenure);
    }

    [Fact]
    public void ASenatorWhoLeftTheHouseCountsOnlySenateTerms()
    {
        var tenure = PublishedOfficialView.Tenure([House(new(2001, 1, 3), new(2007, 1, 3)), Senate(new(2007, 1, 4), new(2013, 1, 3)), Senate(new(2013, 1, 3), new(2027, 1, 3))], Release);

        Assert.Equal("In the Senate since January 2007, now in the 2nd term.", tenure);
    }

    [Fact]
    public void AFormerMemberGetsAClosedTenureBecauseNowInTheTermWouldBeFalse()
    {
        var tenure = PublishedOfficialView.Tenure([House(new(2001, 1, 3), new(2003, 1, 3)), House(new(2003, 1, 3), new(2005, 1, 3))], Release);

        Assert.Equal("In the House from January 2001 to January 2005, 2 terms.", tenure);
    }

    [Fact]
    public void AnExecutiveHasNoTenureSentenceBecauseItNamesAChamber()
    {
        Assert.Null(PublishedOfficialView.Tenure([new() { Office = "President", Kind = PublishedOfficeKind.Executive, Start = new(2025, 1, 20) }], Release));
    }

    [Fact]
    public void TheLedeReusesTheCardWordingForTheRolePartyAndTenure()
    {
        var lede = PublishedOfficialView.Lede([House(new(1997, 1, 7), new(2027, 1, 3))], Release);

        Assert.Equal("Representative for WA, district 9. Party: Democrat. In the House since January 1997, now in the 1st term.", lede);
    }

    [Fact]
    public void TheLedeOfAFormerMemberSaysFormer()
    {
        var lede = PublishedOfficialView.Lede([House(new(1997, 1, 7), new(2003, 1, 3))], Release);

        Assert.StartsWith("Former Representative for WA, district 9.", lede, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOfficialWithNoTermHasNoLedeSoTheHeaderShowsNoEmptyLine()
    {
        Assert.Null(PublishedOfficialView.Lede([], Release));
    }

    private static PublishedOfficeTerm House(DateOnly start, DateOnly? end) => new()
    {
        Office = "rep",
        Kind = PublishedOfficeKind.Representative,
        State = "WA",
        District = 9,
        Party = "Democrat",
        Start = start,
        End = end
    };

    private static PublishedOfficeTerm Senate(DateOnly start, DateOnly? end) => new()
    {
        Office = "sen",
        Kind = PublishedOfficeKind.Senator,
        State = "WA",
        Party = "Democrat",
        Start = start,
        End = end
    };
}
