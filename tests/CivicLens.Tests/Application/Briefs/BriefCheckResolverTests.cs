using CivicLens.Application.Briefs;
using CivicLens.Tests.Fixtures;
using static CivicLens.Tests.Application.Briefs.FakeBriefCheckModel;

namespace CivicLens.Tests.Application.Briefs;

public sealed class BriefCheckResolverTests
{
    private static readonly string Note = "The paragraph says more than the record says.";

    [Fact]
    public void AnAcceptedAnswerBindsItsFlagsToTheRevisionThatWasChecked()
    {
        var revision = BriefFixtures.Revision(number: 4);

        var resolution = BriefCheckResolver.Resolve(Output("NeedsAttention", (1, "overstated", "R2", Note), (0, "other", null, Note)), revision);

        Assert.True(resolution.IsAccepted);
        var result = resolution.Accepted!;
        Assert.Equal((4, BriefCheckRecommendation.NeedsAttention), (result.RevisionNumber, result.Recommendation));
        Assert.Equal([(1, BriefCheckFlagCode.Overstated, "R2"), (0, BriefCheckFlagCode.Other, null)],
            result.Flags.Select(flag => (flag.ParagraphIndex, flag.Code, flag.CitationId)));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(-1)]
    public void AParagraphIndexOutsideTheBriefIsRejectedBecauseTheFlagWouldPointAtNoText(int index)
    {
        var resolution = BriefCheckResolver.Resolve(Output("NeedsAttention", (index, "overstated", null, Note)), BriefFixtures.Revision());

        Assert.False(resolution.IsAccepted);
        Assert.Contains("paragraph", Assert.Single(resolution.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void ACodeOutsideTheClosedListIsRejectedBecauseThePanelNamesOnlyKnownProblems()
    {
        var resolution = BriefCheckResolver.Resolve(Output("NeedsAttention", (0, "tooLong", null, Note)), BriefFixtures.Revision());

        Assert.False(resolution.IsAccepted);
        Assert.Contains("tooLong", Assert.Single(resolution.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void ANumberIsNotACodeEvenWhenItNamesAnEnumMember()
    {
        var resolution = BriefCheckResolver.Resolve(Output("NeedsAttention", (0, "1", null, Note)), BriefFixtures.Revision());

        Assert.False(resolution.IsAccepted);
    }

    [Fact]
    public void AnIdTheBriefDoesNotListIsACitationErrorSoTheRunShowsTheCitationOutcome()
    {
        var resolution = BriefCheckResolver.Resolve(Output("NeedsAttention", (0, "unsupportedByCitation", "R9", Note)), BriefFixtures.Revision());

        Assert.False(resolution.IsAccepted);
        Assert.True(resolution.HasCitationErrors);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ANoteWithoutTextIsRejected(string note)
    {
        Assert.False(BriefCheckResolver.Resolve(Output("NeedsAttention", (0, "other", null, note)), BriefFixtures.Revision()).IsAccepted);
    }

    [Fact]
    public void ANoteOverTheLimitIsRejectedAndANoteAtTheLimitPasses()
    {
        var revision = BriefFixtures.Revision();

        Assert.False(BriefCheckResolver.Resolve(Output("NeedsAttention", (0, "other", null, new string('n', 301))), revision).IsAccepted);
        Assert.True(BriefCheckResolver.Resolve(Output("NeedsAttention", (0, "other", null, new string('n', 300))), revision).IsAccepted);
    }

    [Fact]
    public void ARecommendationToApproveWithFlagsIsRejectedBecauseThePanelCouldNotSayWhichToTrust()
    {
        var resolution = BriefCheckResolver.Resolve(Output("ApproveRecommended", (0, "other", null, Note)), BriefFixtures.Revision());

        Assert.False(resolution.IsAccepted);
    }

    [Fact]
    public void ARecommendationForAttentionWithoutFlagsIsRejectedBecauseThePersonWouldHaveNothingToLookAt()
    {
        Assert.False(BriefCheckResolver.Resolve(Output("NeedsAttention"), BriefFixtures.Revision()).IsAccepted);
    }

    [Fact]
    public void ACleanAnswerHasNoFlags()
    {
        var resolution = BriefCheckResolver.Resolve(Output("ApproveRecommended"), BriefFixtures.Revision());

        Assert.True(resolution.IsAccepted);
        Assert.Empty(resolution.Accepted!.Flags);
    }

    [Fact]
    public void MoreFlagsThanTheLimitAreRejected()
    {
        var flags = Enumerable.Range(0, BriefCheckResult.MaximumFlags + 1).Select(_ => (0, "other", (string?)null, Note)).ToArray();

        Assert.False(BriefCheckResolver.Resolve(Output("NeedsAttention", flags), BriefFixtures.Revision()).IsAccepted);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"recommendation\":\"Maybe\",\"flags\":[]}")]
    [InlineData("{\"recommendation\":\"ApproveRecommended\",\"flags\":[],\"confidence\":0.9}")]
    public void AnAnswerThatBreaksTheSchemaIsRejectedAndStoresNoConfidence(string json)
    {
        Assert.False(BriefCheckResolver.Resolve(json, BriefFixtures.Revision()).IsAccepted);
    }
}
