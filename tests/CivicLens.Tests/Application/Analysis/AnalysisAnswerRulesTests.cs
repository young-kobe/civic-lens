using CivicLens.Application.Analysis;

namespace CivicLens.Tests.Application.Analysis;

public sealed class AnalysisAnswerRulesTests
{
    [Fact]
    public void ATooLongTextRejectionStatesTheActualLengthSoTheRetryCanShortenIt()
    {
        var errors = new List<string>();

        AnalysisAnswerRules.CheckText(new string('a', 279), 256, "headline", required: true, errors);

        Assert.Equal(["headline has 279 characters. Write at most 256."], errors);
    }

    [Fact]
    public void ANulCharacterRejectionDoesNotTalkAboutLength()
    {
        var errors = new List<string>();

        AnalysisAnswerRules.CheckText("a\0b", 256, "headline", required: true, errors);

        Assert.Equal(["headline must contain no NUL character."], errors);
    }

    [Fact]
    public void BoundReplacesEachNulCharacterWithTheReplacementCharacterBecauseAnErrorQuotesModelTextAndPostgresTextCannotHoldNul()
    {
        var errors = AnalysisAnswerRules.Bound(["The ID \"a\0b\0\" is not listed.", "Plain error."]);

        Assert.Equal(["The ID \"a\uFFFDb\uFFFD\" is not listed.", "Plain error."], errors.ToArray());
    }

    [Fact]
    public void ATextWithinTheLimitIsAccepted()
    {
        var errors = new List<string>();

        AnalysisAnswerRules.CheckText(new string('a', 256), 256, "headline", required: true, errors);

        Assert.Empty(errors);
    }

    [Fact]
    public void ARequiredBlankTextOverTheLimitGetsOnlyTheRequiredMessage()
    {
        var errors = new List<string>();

        AnalysisAnswerRules.CheckText(new string(' ', 300), 256, "headline", required: true, errors);

        Assert.Equal(["headline is required."], errors);
    }
}
