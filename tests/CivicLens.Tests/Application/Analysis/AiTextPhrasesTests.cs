using CivicLens.Application.Analysis;

namespace CivicLens.Tests.Application.Analysis;

public sealed class AiTextPhrasesTests
{
    [Theory]
    [InlineData("This order aims to cut costs.", "aims to")]
    [InlineData("The Secretary Seeks  To review each rule.", "seeks to")]
    [InlineData("The review is intended to finish soon.", "intended to")]
    [InlineData("The bill helps renters.", "the bill helps")]
    [InlineData("This Measure supports farms.", "this measure supports")]
    [InlineData("The order addresses trade.", "the order addresses")]
    [InlineData("The document improves safety.", "the document improves")]
    public void ASentenceWithAListedPhraseGetsOneReasonThatNamesThePhrase(string sentence, string phrase)
    {
        var error = Assert.Single(AiTextPhrases.Errors([sentence]));

        Assert.Contains($"The sentence \"{sentence}\" uses the phrase \"{phrase}\".", error, StringComparison.Ordinal);
        Assert.True(AiTextPhrases.IsReason(error));
    }

    [Theory]
    [InlineData("The Secretary claims to review each rule.")]
    [InlineData("The order names an aim to review each rule.")]
    [InlineData("Senator Warren supports the bill.")]
    [InlineData("The program helps renters pay rent.")]
    [InlineData("The bill gives renters $5 million in aid.")]
    public void ASentenceWithNoListedPhraseGetsNoReason(string sentence) => Assert.Empty(AiTextPhrases.Errors([sentence]));

    [Fact]
    public void EachSentenceOfEachPieceIsCheckedAndOnlyTheSentenceWithThePhraseIsNamed()
    {
        var errors = AiTextPhrases.Errors(["H.R. 1 gives renters aid.", "It starts in 2026. The bill helps renters.", null]).ToArray();

        Assert.Contains("The sentence \"The bill helps renters.\" uses the phrase \"the bill helps\". Write who it affects and what it changes.",
            Assert.Single(errors), StringComparison.Ordinal);
    }

    [Fact]
    public void TheAimReasonAsksForWhatTheTextDoes() =>
        Assert.EndsWith("Write what it does, and not what it aims at.", Assert.Single(AiTextPhrases.Errors(["It aims to help."])), StringComparison.Ordinal);

    [Fact]
    public void ThePromptRuleNamesEveryPhraseAndStatesTheCodeRule() =>
        Assert.Equal("Never use the phrases \"aims to\", \"seeks to\", or \"intended to\". Never write that the bill helps, supports, addresses, or improves " +
            "something. Write who it affects and what it changes. Code rejects a sentence with one of these phrases.", AiTextPhrases.Rule("the bill"));
}
