using System.Collections.Immutable;
using CivicLens.Application.Votes;
using CivicLens.Core.Documents;
using CivicLens.Core.Votes;

namespace CivicLens.Tests.Application.Votes;

public sealed class VoteFactsTests
{
    private static ImmutableArray<VoteFact> Facts(params string[] texts) => [.. texts.Select(text => new VoteFact(text, new SourceSpan(new string('a', 64), 0, 1)))];

    [Fact]
    public void TheFirstFactWithThePrefixGivesTheQuestionAndTheResultBecauseRepeatedLinesMustNotChangeTheAnswer()
    {
        var facts = Facts("Started: 2025-04-10", "Vote title: Question: not the question", "Question: First", "Question: Second", "Result: Passed", "Result: Later");

        Assert.Equal("First", VoteFacts.QuestionOf(facts));
        Assert.Equal("Passed", VoteFacts.ResultOf(facts));
    }

    [Fact]
    public void AVoteWithNoQuestionLineHasNoQuestionBecauseTheSourceCanOmitIt()
    {
        Assert.Null(VoteFacts.QuestionOf(Facts("Result: Failed")));
    }

    [Fact]
    public void AVoteWithNoResultLineIsRefusedBecauseEveryPublishedVoteNeedsOne()
    {
        Assert.Throws<ArgumentException>(() => VoteFacts.ResultOf(Facts("Question: On Passage")));
    }
}
