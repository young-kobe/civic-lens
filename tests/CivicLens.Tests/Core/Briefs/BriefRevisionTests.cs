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

    [Fact]
    public void APersonEditsTheHeadlineTheParagraphsAndTheirListedCitationsButNeverTheWeekTheFingerprintOrTheRecordCounts()
    {
        var previous = BriefFixtures.Revision();
        var edit = PersonEdit(previous);

        edit.ValidatePersonEditOf(previous);
        Assert.Throws<ArgumentException>(() => (edit with { Fingerprint = new string('e', 64) }).ValidatePersonEditOf(previous));
        Assert.Throws<ArgumentException>(() => (edit with { Window = previous.Window with { CutoffReleaseNumber = 9 } }).ValidatePersonEditOf(previous));
        Assert.Throws<ArgumentException>(() => (edit with { Coverage = previous.Coverage with { Considered = 99 } }).ValidatePersonEditOf(previous));
        Assert.Throws<ArgumentException>(() => (edit with { RevisionNumber = 3 }).ValidatePersonEditOf(previous));
        Assert.Throws<ArgumentException>(() => (edit with { AuthorSubject = ReviewAuthor.AnalysisSubject }).ValidatePersonEditOf(previous));
    }

    [Fact]
    public void APersonKeepsOnlyCitationsThatTheBriefAlreadyListsSoNoPersonCanCiteARecordTheDrafterNeverSelected()
    {
        var previous = BriefFixtures.Revision();
        var foreign = new BriefCitation("R3", BriefCitationKind.Action, new string('3', 64), 1);
        var edit = PersonEdit(previous) with
        {
            Paragraphs = [new("Warren backed a rent cap.", ["R1"]), new("A rule changed.", ["R2", "R3"])],
            Citations = [.. previous.Citations, foreign],
            Coverage = previous.Coverage with { Cited = 3 }
        };

        Assert.Throws<ArgumentException>(() => edit.ValidatePersonEditOf(previous));
    }

    [Fact]
    public void APersonMayDropACitationThatNoParagraphUsesAnymoreWhenTheCoverageCountsTheRest()
    {
        var previous = BriefFixtures.Revision();
        var edit = PersonEdit(previous) with
        {
            Paragraphs = [new("Warren backed a rent cap.", ["R1"])],
            Citations = [previous.Citations[0]],
            Coverage = previous.Coverage with { Cited = 1 }
        };

        edit.ValidatePersonEditOf(previous);
        Assert.Throws<ArgumentException>(() => (edit with { Coverage = previous.Coverage }).ValidatePersonEditOf(previous));
    }

    [Fact]
    public void APersonMayKeepOrClearTheSuggestionButNeverChangeItEvenWhenTheCopyIsEqualInContent()
    {
        var suggestion = Suggestion();
        var previous = BriefFixtures.Revision(2) with { TextOrigin = ReviewTextOrigin.Person, Suggestion = suggestion };
        var edit = PersonEdit(previous) with { RevisionNumber = 3 };
        var sameContent = suggestion with { Paragraphs = [.. suggestion.Paragraphs], Citations = [.. suggestion.Citations] };

        (edit with { Suggestion = suggestion }).ValidatePersonEditOf(previous);
        (edit with { Suggestion = sameContent }).ValidatePersonEditOf(previous);
        (edit with { Suggestion = null }).ValidatePersonEditOf(previous);
        Assert.Throws<ArgumentException>(() => (edit with { Suggestion = suggestion with { Headline = "Changed" } }).ValidatePersonEditOf(previous));
        Assert.Throws<ArgumentException>(() => (edit with { Suggestion = suggestion with { Paragraphs = [new("Other.", ["R1"])] } }).ValidatePersonEditOf(previous));
    }

    [Fact]
    public void APersonAdoptsTheSuggestionOnlyAsOneUnitOfHeadlineParagraphsAndCitationsAndTheSuggestionIsCleared()
    {
        var previous = WithReboundSuggestion();
        var suggestion = previous.Suggestion!;
        var adoption = Adoption(previous);

        adoption.ValidatePersonEditOf(previous);
        Assert.Throws<ArgumentException>(() => (adoption with { Suggestion = suggestion }).ValidatePersonEditOf(previous));
        Assert.Throws<ArgumentException>(() => (adoption with { Headline = "Mixed headline" }).ValidatePersonEditOf(previous));
        Assert.Throws<ArgumentException>(() => (adoption with { Paragraphs = [new("Mixed paragraph.", ["R1"])] }).ValidatePersonEditOf(previous));
        Assert.Throws<ArgumentException>(() => (adoption with { Coverage = adoption.Coverage with { Cited = 2 } }).ValidatePersonEditOf(previous));
    }

    [Fact]
    public void AdoptingTheSuggestionWithAlteredCitationsIsRefusedBecauseThePersonMayNotRebindACitationToAnotherRecord()
    {
        var previous = WithReboundSuggestion();
        var rebound = previous.Suggestion!.Citations[0] with { RecordId = new string('8', 64) };

        Assert.Throws<ArgumentException>(() => (Adoption(previous) with { Citations = [rebound] }).ValidatePersonEditOf(previous));
    }

    private static BriefRevision WithReboundSuggestion()
    {
        var suggestion = Suggestion() with { Citations = [BriefFixtures.Revision().Citations[0] with { RecordId = new string('9', 64) }] };
        return BriefFixtures.Revision(2) with { TextOrigin = ReviewTextOrigin.Person, Suggestion = suggestion };
    }

    private static BriefRevision Adoption(BriefRevision previous) => previous with
    {
        RevisionNumber = previous.RevisionNumber + 1,
        AuthorSubject = "auth0|editor",
        TextOrigin = ReviewTextOrigin.Person,
        Suggestion = null,
        Headline = previous.Suggestion!.Headline,
        Paragraphs = previous.Suggestion.Paragraphs,
        Citations = previous.Suggestion.Citations,
        Coverage = previous.Coverage with { Cited = previous.Suggestion.Citations.Length }
    };

    private static BriefRevision PersonEdit(BriefRevision previous) => previous with
    {
        RevisionNumber = previous.RevisionNumber + 1,
        AuthorSubject = "auth0|editor",
        TextOrigin = ReviewTextOrigin.Person,
        Suggestion = null,
        Headline = "A person headline",
        Paragraphs = [new("A person paragraph.", ["R1", "R2"])]
    };

    private static BriefTextSuggestion Suggestion() => new("Another headline", [new("Another paragraph.", ["R1"])],
        [BriefFixtures.Revision().Citations[0]], new string('1', 32), new string('2', 64));
}
