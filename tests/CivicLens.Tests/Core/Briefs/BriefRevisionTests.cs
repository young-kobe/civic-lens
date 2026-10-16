using System.Collections.Immutable;
using CivicLens.Core.Briefs;
using CivicLens.Core.Review;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Core.Briefs;

public sealed class BriefRevisionTests
{
    [Fact]
    public void AWellFormedRevisionPassesTheShapeCheck()
    {
        BriefFixtures.Revision().ValidateShape();
    }

    [Fact]
    public void TheRecordIdMustNameTheWeekSoNoTwoBriefsCoverTheSameWeek()
    {
        var other = BriefFixtures.Revision() with { RecordId = new string('c', 64) };

        Assert.Throws<ArgumentException>(other.ValidateShape);
    }

    [Theory]
    [InlineData(166)]
    [InlineData(170)]
    public void AWeekLastsOnlyAsLongAsAClockChangeAllows(int hours)
    {
        var revision = BriefFixtures.Revision();
        var window = revision.Window with { EndUtcTicks = revision.Window.StartUtcTicks + TimeSpan.FromHours(hours).Ticks };

        Assert.Throws<ArgumentException>((revision with { Window = window }).ValidateShape);
    }

    [Theory]
    [InlineData(167)]
    [InlineData(168)]
    [InlineData(169)]
    public void ASpringWeekOfAHundredSixtySevenHoursAndAFallWeekOfAHundredSixtyNineAreBothValid(int hours)
    {
        var revision = BriefFixtures.Revision();
        var window = revision.Window with { EndUtcTicks = revision.Window.StartUtcTicks + TimeSpan.FromHours(hours).Ticks };

        (revision with { Window = window }).ValidateShape();
    }

    [Fact]
    public void TheFirstReleaseCannotFollowTheCutoffRelease()
    {
        var revision = BriefFixtures.Revision();

        Assert.Throws<ArgumentException>((revision with { Window = revision.Window with { FirstReleaseNumber = 4 } }).ValidateShape);
    }

    [Fact]
    public void EveryParagraphNeedsAtLeastOneCitationId()
    {
        var revision = BriefFixtures.Revision() with { Paragraphs = [new("Uncited.", []), new("Cited.", ["R1", "R2"])] };

        Assert.Throws<ArgumentException>(revision.ValidateShape);
    }

    [Fact]
    public void AParagraphCannotCiteTheSameIdTwiceOrAnIdTheBriefDoesNotList()
    {
        var repeated = BriefFixtures.Revision() with { Paragraphs = [new("Text.", ["R1", "R1"]), new("Other.", ["R2"])] };
        var unknown = BriefFixtures.Revision() with { Paragraphs = [new("Text.", ["R1", "R9"]), new("Other.", ["R2"])] };

        Assert.Throws<ArgumentException>(repeated.ValidateShape);
        Assert.Throws<ArgumentException>(unknown.ValidateShape);
    }

    [Fact]
    public void ACitationNoParagraphUsesIsRefusedSoTheCitedCountStaysHonest()
    {
        var revision = BriefFixtures.Revision() with { Paragraphs = [new("Only R1.", ["R1"])] };

        Assert.Throws<ArgumentException>(revision.ValidateShape);
    }

    [Fact]
    public void ARecordIsCitedOnceUnderOneId()
    {
        var revision = BriefFixtures.Revision();
        var twice = revision with
        {
            Citations = [revision.Citations[0], revision.Citations[1] with { Kind = revision.Citations[0].Kind, RecordId = revision.Citations[0].RecordId }]
        };

        Assert.Throws<ArgumentException>(twice.ValidateShape);
    }

    [Fact]
    public void CoverageMustCountTheListedCitationsAndCannotSendMoreThanItConsidered()
    {
        var revision = BriefFixtures.Revision();

        Assert.Throws<ArgumentException>((revision with { Coverage = new(5, 4, 3) }).ValidateShape);
        Assert.Throws<ArgumentException>((revision with { Coverage = new(3, 4, 2) }).ValidateShape);
        Assert.Equal(1, revision.Coverage.Omitted);
    }

    [Fact]
    public void AnAnalysisRevisionFollowsTheCurrentRevisionAndKeepsModelTextWithoutASuggestion()
    {
        var first = BriefFixtures.Revision();
        var second = BriefFixtures.Revision(2);

        second.ValidateAsAnalysisRevision(first);
        Assert.Throws<ArgumentException>(() => BriefFixtures.Revision(3).ValidateAsAnalysisRevision(first));
        Assert.Throws<ArgumentException>(() => (second with { Suggestion = Suggestion() }).ValidateAsAnalysisRevision(first));
    }

    [Fact]
    public void AnAnalysisRevisionNeverOverwritesTheTextOfAPersonAndOffersItsOwnAsASuggestion()
    {
        var person = BriefFixtures.Revision() with { AuthorSubject = "auth0|editor", TextOrigin = ReviewTextOrigin.Person };
        var overwriting = BriefFixtures.Revision(2);
        var keeping = overwriting with { TextOrigin = ReviewTextOrigin.Person, Suggestion = Suggestion() };

        Assert.Throws<ArgumentException>(() => overwriting.ValidateAsAnalysisRevision(person));
        keeping.ValidateAsAnalysisRevision(person);
    }

    [Fact]
    public void ASuggestionCitesOnlyTheIdsThatItsOwnCitationsListBecauseItsIdsAreNotThoseOfTheText()
    {
        var suggestion = Suggestion() with { Paragraphs = [new("Another paragraph.", ["R2"])] };
        var revision = BriefFixtures.Revision(2) with { TextOrigin = ReviewTextOrigin.Person, Suggestion = suggestion };

        Assert.Throws<ArgumentException>(revision.ValidateShape);
    }

    [Fact]
    public void APersonRevisionHoldsPersonText()
    {
        var revision = BriefFixtures.Revision(2, "auth0|editor");

        Assert.Throws<ArgumentException>(revision.ValidateShape);
    }

    private static BriefTextSuggestion Suggestion() => new("Another headline", [new("Another paragraph.", ["R1"])],
        [BriefFixtures.Revision().Citations[0]], new string('1', 32), new string('2', 64));
}
