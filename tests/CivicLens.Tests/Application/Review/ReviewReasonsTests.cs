using CivicLens.Application.Review;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.Review;

public sealed class ReviewReasonsTests
{
    [Theory]
    [InlineData(PublishedRecordKind.Statement, "NotAStatement,WrongSpeaker,IndexPage,LowSubstance,InaccurateSummary,Duplicate")]
    [InlineData(PublishedRecordKind.Relationship, "NotRelated,Duplicate")]
    [InlineData(PublishedRecordKind.DocumentChange, "InaccurateSummary,Duplicate,LowSubstance")]
    [InlineData(PublishedRecordKind.Brief, "InaccurateSummary,Duplicate,LowSubstance")]
    [InlineData(PublishedRecordKind.Action, "InaccurateSummary,Duplicate")]
    [InlineData(PublishedRecordKind.Vote, "InaccurateSummary,Duplicate")]
    [InlineData(PublishedRecordKind.ExecutiveAction, "InaccurateSummary,Duplicate")]
    public void EachKindOffersOnlyTheReasonsThatCanApplyToItBecauseAWrongReasonWouldSkewTheCountsByCause(PublishedRecordKind kind, string expected) =>
        Assert.Equal(expected, string.Join(',', ReviewReasons.For(kind)));

    [Fact]
    public void EveryReasonCodeBelongsToAtLeastOneKindSoNoCodeIsDeadCode() =>
        Assert.All(Enum.GetValues<ReviewReasonCode>(), code =>
            Assert.Contains(Enum.GetValues<PublishedRecordKind>(), kind => ReviewReasons.For(kind).Contains(code)));

    [Fact]
    public void ARejectionWithAReasonOfAnotherKindIsRefusedAndNoReasonIsLeftToTheDecisionPolicy()
    {
        Assert.Throws<ArgumentException>(() => ReviewReasons.Require(PublishedRecordKind.Statement, ReviewReasonCode.NotRelated));
        Assert.Throws<ArgumentException>(() => ReviewReasons.Require(PublishedRecordKind.Relationship, ReviewReasonCode.WrongSpeaker));
        ReviewReasons.Require(PublishedRecordKind.Relationship, ReviewReasonCode.NotRelated);
        ReviewReasons.Require(PublishedRecordKind.Vote, null);
    }
}
