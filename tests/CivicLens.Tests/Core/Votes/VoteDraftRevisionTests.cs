using CivicLens.Core.Documents;
using CivicLens.Core.Votes;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Core.Votes;

public sealed class VoteDraftRevisionTests
{
    private static readonly string ExtractionId = new('c', 64);

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

    [Theory]
    [InlineData("")]
    [InlineData("bad\0text")]
    public void HeadlineAndSummaryNeedTextWithoutNul(string text)
    {
        Assert.Throws<ArgumentException>(() => (Revision() with { Headline = text }).ValidateShape());
        Assert.Throws<ArgumentException>(() => (Revision() with { Summary = text }).ValidateShape());
    }

    private static MemberVote Member() => new("aderholt", "A000055", "Yea", new SourceSpan(ExtractionId, 30, 40));

    private static VoteDraftRevision Revision(int number = 1, string author = "civic-lens:analysis") => new(
        VoteWorld.RecordId, number, author, VoteWorld.UpdatedAt.AddMinutes(number), "The House voted", "The House decided a question.",
        "congress-house-votes", 119, 1, 100, "hconres", 14, VoteWorld.CandidateId, VoteWorld.UpdatedAt,
        [new("Result: Passed", new SourceSpan(ExtractionId, 0, 14))], VoteKind.Passage, new SourceSpan(ExtractionId, 15, 20), [Member()], ["rent"], new string('e', 64));
}
