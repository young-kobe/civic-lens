using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Core.Votes;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Core.Votes;

public sealed class VoteDraftRevisionTests
{
    private static readonly string ExtractionId = new('c', 64);

    [Fact]
    public void ARevisionNeedsADefinedChamberBecauseRollNumbersRepeatAcrossChambers()
    {
        Revision().ValidateShape();

        Assert.Throws<ArgumentException>(() => (Revision() with { Chamber = (Chamber)9 }).ValidateShape());
    }

    [Theory]
    [InlineData(Chamber.House, VoteSource.SenateGov)]
    [InlineData(Chamber.House, VoteSource.Voteview)]
    [InlineData(Chamber.Senate, VoteSource.CongressGov)]
    [InlineData(Chamber.Senate, (VoteSource)9)]
    public void AVoteSourceThatCannotHaveCarriedTheChamberIsRefusedSoAHouseVoteNeverNamesVoteview(Chamber chamber, VoteSource source)
    {
        Assert.Throws<ArgumentException>(() => (Revision() with { Chamber = chamber, Source = source }).ValidateShape());
        (Revision() with { Chamber = Chamber.Senate, Source = VoteSource.Voteview }).ValidateShape();
    }

    [Fact]
    public void AnAnalysisRevisionFollowsTheCurrentOneAndAFirstRevisionIsNumberOne()
    {
        Revision().ValidateAsAnalysisRevision(null);
        Revision(2).ValidateAsAnalysisRevision(Revision());

        Assert.Throws<ArgumentException>(() => Revision(2).ValidateAsAnalysisRevision(null));
        Assert.Throws<ArgumentException>(() => Revision(3).ValidateAsAnalysisRevision(Revision()));
        Assert.Throws<ArgumentException>(() => Revision(author: "auth0|person").ValidateAsAnalysisRevision(null));
    }

    [Fact]
    public void ARecordNeedsAtLeastOneMemberVoteBecauseItExistsToListTheTrackedVotes()
    {
        Assert.Throws<ArgumentException>(() => (Revision() with { Members = [] }).ValidateShape());
        Assert.Throws<ArgumentException>(() => (Revision() with { Members = default }).ValidateShape());
        Assert.Throws<ArgumentException>(() => (Revision() with { Members = [.. Enumerable.Repeat(Member(), VoteDraftRevision.MaximumMembers + 1)] }).ValidateShape());
    }

    [Fact]
    public void ARevisionNeedsAnAuthorAndAUtcCreationTimeBecauseEveryRevisionKindSharesOneHeaderRule()
    {
        Assert.Throws<ArgumentException>(() => (Revision() with { AuthorSubject = " " }).ValidateShape());
        Assert.Throws<ArgumentException>(() => (Revision() with { CreatedAtUtc = Revision().CreatedAtUtc.ToOffset(TimeSpan.FromHours(2)) }).ValidateShape());
    }

    [Fact]
    public void ACastOrBillTypeOverTheLimitIsRefusedBecauseThePublishedVoteRefusesItToo()
    {
        Assert.Throws<ArgumentException>(() => (Revision() with { Members = [Member() with { Cast = new string('y', VoteDraftRevision.MaximumCastLength + 1) }] }).ValidateShape());
        Assert.Throws<ArgumentException>(() => (Revision() with { BillType = new string('H', VoteDraftRevision.MaximumBillTypeLength + 1), BillNumber = 1 }).ValidateShape());
    }

    [Fact]
    public void EveryCitationMustBeABoundedSpanOfAnExtractionSoTheReviewerCanOpenTheLine()
    {
        Assert.Throws<ArgumentException>(() => (Revision() with { Facts = [new("Result: Passed", new SourceSpan(ExtractionId, 0, 0))] }).ValidateShape());
        Assert.Throws<ArgumentException>(() => (Revision() with { Facts = [new("Result: Passed", new SourceSpan("short", 0, 5))] }).ValidateShape());
        Assert.Throws<ArgumentException>(() => (Revision() with { Members = [Member() with { Citation = new SourceSpan(ExtractionId, -1, 5) }] }).ValidateShape());
    }

    [Fact]
    public void AKindOtherThanOtherMustCiteItsQuestionSoTheReviewerCanCheckTheKindAgainstTheSavedLine()
    {
        (Revision() with { Kind = VoteKind.Other, KindCitation = null }).ValidateShape();
        (Revision() with { Kind = VoteKind.Other }).ValidateShape();

        Assert.Throws<ArgumentException>(() => (Revision() with { KindCitation = null }).ValidateShape());
        Assert.Throws<ArgumentException>(() => (Revision() with { KindCitation = new SourceSpan(ExtractionId, 0, 0) }).ValidateShape());
        Assert.Throws<ArgumentException>(() => (Revision() with { Kind = (VoteKind)99 }).ValidateShape());
    }

    [Fact]
    public void TheVoteDayMustCiteItsStartLineSoThePublishedDateIsNeverAnUnbackedClaim()
    {
        Assert.Throws<ArgumentException>(() => (Revision() with { VotedOn = default }).ValidateShape());
        Assert.Throws<ArgumentException>(() => (Revision() with { VotedOnCitation = new SourceSpan(ExtractionId, 0, 0) }).ValidateShape());
    }

    [Fact]
    public void TheCitationsHoldTheVoteDayTheFactsTheKindAndTheMembersInThatOrderSoTheSourceOrderOfAPublishedRecordNeverChanges()
    {
        var revision = Revision();

        Assert.Equal([revision.VotedOnCitation, revision.Facts[0].Citation, revision.KindCitation!, revision.Members[0].Citation], revision.Citations);
        Assert.Equal(3, (Revision() with { KindCitation = null, Kind = VoteKind.Other }).Citations.Count());
    }

    [Fact]
    public void TheIssueRevisionIsAHashAndTheTrackedIssuesAreBoundedSoTheRecordNamesTheRulesThatChoseThem()
    {
        Assert.Throws<ArgumentException>(() => (Revision() with { IssueRevisionId = "short" }).ValidateShape());
        Assert.Throws<ArgumentException>(() => (Revision() with { TrackedIssueIds = default }).ValidateShape());
        Assert.Throws<ArgumentException>(() => (Revision() with { TrackedIssueIds = [.. Enumerable.Repeat("rent", 65)] }).ValidateShape());
        (Revision() with { TrackedIssueIds = [] }).ValidateShape();
    }

    [Fact]
    public void ABillTypeNeedsANumberAndANumberNeedsAType()
    {
        (Revision() with { BillType = null, BillNumber = null }).ValidateShape();

        Assert.Throws<ArgumentException>(() => (Revision() with { BillNumber = null }).ValidateShape());
        Assert.Throws<ArgumentException>(() => (Revision() with { BillType = null }).ValidateShape());
    }

    [Fact]
    public void ARevisionAfterAPersonsTextMustKeepThatTextAndOfferTheModelsAsASuggestion()
    {
        var earlier = Suggestion() with { Headline = "Old", RunId = new string('2', 32) };
        var person = PersonEditOf(Revision(), "Edited headline", "Edited summary.") with { Suggestion = earlier };
        var kept = Revision(3) with { Headline = person.Headline, Summary = person.Summary, TextOrigin = ReviewTextOrigin.Person, Suggestion = Suggestion() };

        kept.ValidateAsAnalysisRevision(person);

        Assert.Throws<ArgumentException>(() => (kept with { Headline = "Overwritten" }).ValidateAsAnalysisRevision(person));
        Assert.Throws<ArgumentException>(() => (kept with { Suggestion = null }).ValidateAsAnalysisRevision(person));
        Assert.Throws<ArgumentException>(() => Revision(3).ValidateAsAnalysisRevision(person));
    }

    [Fact]
    public void ARevisionThatCopiesThePersonsTextIsValidBecauseAMemberVoteChangeNeedsNoNewText()
    {
        var person = PersonEditOf(Revision(), "Edited headline", "Edited summary.") with { Suggestion = Suggestion() };

        (person with { RevisionNumber = 3, AuthorSubject = "civic-lens:analysis", Members = [Member() with { Cast = "Nay" }] }).ValidateAsAnalysisRevision(person);
    }

    [Fact]
    public void ModelTextAfterModelTextCarriesNoSuggestion()
    {
        Assert.Throws<ArgumentException>(() => (Revision(2) with { Suggestion = Suggestion() }).ValidateAsAnalysisRevision(Revision()));
    }

    [Fact]
    public void AVoteReviewerEditsOnlyTheHeadlineAndTheSummaryBecauseCodeOwnsTheDayTheKindTheFactsAndTheMemberVotes()
    {
        var previous = Revision();
        var edit = PersonEditOf(previous, "A better headline", "A better summary.");

        edit.ValidatePersonEditOf(previous);

        Assert.Throws<ArgumentException>(() => (edit with { Members = [Member() with { Cast = "Nay" }] }).ValidatePersonEditOf(previous));
        Assert.Throws<ArgumentException>(() => (edit with { Kind = VoteKind.Other, KindCitation = null }).ValidatePersonEditOf(previous));
        Assert.Throws<ArgumentException>(() => (edit with { Facts = [new("Result: Failed", new SourceSpan(ExtractionId, 0, 14))] }).ValidatePersonEditOf(previous));
        Assert.Throws<ArgumentException>(() => (edit with { TrackedIssueIds = ["extra"] }).ValidatePersonEditOf(previous));
        Assert.Throws<ArgumentException>(() => (edit with { BillType = "hr", BillNumber = 1 }).ValidatePersonEditOf(previous));
        Assert.Throws<ArgumentException>(() => (edit with { KindCitation = new SourceSpan(ExtractionId, 16, 20) }).ValidatePersonEditOf(previous));
        Assert.Throws<ArgumentException>(() => (edit with { CandidateId = new string('f', 64) }).ValidatePersonEditOf(previous));
        Assert.Throws<ArgumentException>(() => (edit with { IssueRevisionId = new string('f', 64) }).ValidatePersonEditOf(previous));
        Assert.Throws<ArgumentException>(() => (edit with { SourceId = "other-source" }).ValidatePersonEditOf(previous));
        Assert.Throws<ArgumentException>(() => (edit with { Chamber = Chamber.Senate }).ValidatePersonEditOf(previous));
        Assert.Throws<ArgumentException>(() => (edit with { Congress = 118 }).ValidatePersonEditOf(previous));
        Assert.Throws<ArgumentException>(() => (edit with { SessionNumber = 2 }).ValidatePersonEditOf(previous));
        Assert.Throws<ArgumentException>(() => (edit with { RollCallNumber = 101 }).ValidatePersonEditOf(previous));
        Assert.Throws<ArgumentException>(() => (edit with { CandidateUpdateDate = previous.CandidateUpdateDate.AddDays(1) }).ValidatePersonEditOf(previous));
        Assert.Throws<ArgumentException>(() => (edit with { VotedOn = previous.VotedOn.AddDays(1) }).ValidatePersonEditOf(previous));
        Assert.Throws<ArgumentException>(() => (edit with { VotedOnCitation = new SourceSpan(ExtractionId, 1, 10) }).ValidatePersonEditOf(previous));
    }

    [Fact]
    public void APersonCannotEditUnderTheAnalysisAuthorOrSkipARevisionOrMarkTheTextAsModelText()
    {
        var previous = Revision();

        Assert.Throws<ArgumentException>(() => (PersonEditOf(previous, "h", "s") with { AuthorSubject = "civic-lens:analysis" }).ValidatePersonEditOf(previous));
        Assert.Throws<ArgumentException>(() => (PersonEditOf(previous, "h", "s") with { RevisionNumber = 3 }).ValidatePersonEditOf(previous));
        Assert.Throws<ArgumentException>(() => (PersonEditOf(previous, "h", "s") with { TextOrigin = ReviewTextOrigin.Model }).ValidatePersonEditOf(previous));
    }

    [Fact]
    public void APersonMayKeepOrClearTheSuggestionButNeverChangeItBecauseOnlyTheModelWritesSuggestions()
    {
        var previous = Revision(2) with { TextOrigin = ReviewTextOrigin.Person, Suggestion = Suggestion() };
        var edit = PersonEditOf(previous, "Mine", "My summary.");

        edit.ValidatePersonEditOf(previous);
        (edit with { Suggestion = null }).ValidatePersonEditOf(previous);

        Assert.Throws<ArgumentException>(() => (edit with { Suggestion = Suggestion() with { Headline = "Other" } }).ValidatePersonEditOf(previous));
        Assert.Throws<ArgumentException>(() => (PersonEditOf(Revision(), "h", "s") with { Suggestion = Suggestion() }).ValidatePersonEditOf(Revision()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad\0text")]
    public void HeadlineAndSummaryNeedTextWithoutNul(string text)
    {
        Assert.Throws<ArgumentException>(() => (Revision() with { Headline = text }).ValidateShape());
        Assert.Throws<ArgumentException>(() => (Revision() with { Summary = text }).ValidateShape());
    }

    private static ReviewTextSuggestion Suggestion() => new("New", "New summary.", new string('1', 32), new string('d', 64));

    private static VoteDraftRevision PersonEditOf(VoteDraftRevision previous, string headline, string summary) => previous with
    {
        RevisionNumber = previous.RevisionNumber + 1,
        AuthorSubject = "auth0|editor",
        Headline = headline,
        Summary = summary,
        TextOrigin = ReviewTextOrigin.Person
    };

    [Fact]
    public void TheOfficialIdsNameEachMemberOnceInTheOrderOfTheMemberVotes()
    {
        var revision = Revision() with { Members = [Member(), Member() with { PersonId = "markey" }, Member()] };

        Assert.Equal(["aderholt", "markey"], revision.OfficialIds().ToArray());
    }

    private static MemberVote Member() => new("aderholt", "A000055", "Yea", new SourceSpan(ExtractionId, 30, 40));

    private static VoteDraftRevision Revision(int number = 1, string author = "civic-lens:analysis") => new(
        VoteWorld.RecordId, number, author, VoteWorld.UpdatedAt.AddMinutes(number), "The House voted", "The House decided a question.", ReviewTextOrigin.Model, null,
        "congress-house-votes", VoteSource.CongressGov, Chamber.House, 119, 1, 100, "hconres", 14, VoteWorld.CandidateId, VoteWorld.UpdatedAt,
        [new("Result: Passed", new SourceSpan(ExtractionId, 0, 14))], new DateOnly(2025, 4, 10), new SourceSpan(ExtractionId, 30, 28),
        VoteKind.Passage, new SourceSpan(ExtractionId, 15, 20), [Member()], ["rent"], new string('e', 64));
}
