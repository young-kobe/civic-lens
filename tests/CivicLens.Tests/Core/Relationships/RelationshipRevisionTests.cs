using CivicLens.Core.Documents;
using CivicLens.Core.Relationships;
using CivicLens.Core.Review;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Core.Relationships;

public sealed class RelationshipRevisionTests
{
    private static RelationshipRevision Revision(RelationshipType type = RelationshipType.DescribesOwnBill,
        RelationshipRecordKind target = RelationshipRecordKind.Action) => new(
        new string('f', 64), 1, ReviewAuthor.AnalysisSubject, RelationshipFixtures.Created,
        new(RelationshipRecordKind.Statement, RelationshipFixtures.StatementId, 2), new(target, RelationshipFixtures.ActionId, 3),
        new(RelationshipMatchBasis.BillNumber, RelationshipFixtures.Span(RelationshipFixtures.BillWords)), type, "She says she introduced it.",
        RelationshipFixtures.Span(RelationshipFixtures.BillSentence), "F2");

    [Fact]
    public void AValidRevisionPasses() => Revision().ValidateShape();

    [Theory]
    [InlineData(RelationshipType.ClaimsCreditFor, RelationshipRecordKind.Vote)]
    [InlineData(RelationshipType.StatementAboutVote, RelationshipRecordKind.Action)]
    public void ATypeThatDoesNotFitTheKindOfTheTargetIsRefused(RelationshipType type, RelationshipRecordKind target) =>
        Assert.Throws<ArgumentException>(() => Revision(type, target).ValidateShape());

    [Fact]
    public void EveryTypeFitsAtLeastOneKindAndTheVoteKindHasOnlyTheVoteType()
    {
        Assert.Equal([RelationshipType.StatementAboutVote], RelationshipTypes.For(RelationshipRecordKind.Vote).ToArray());
        Assert.Equal(Enum.GetValues<RelationshipType>().Order(),
            RelationshipTypes.For(RelationshipRecordKind.Action).Concat(RelationshipTypes.For(RelationshipRecordKind.Vote)).Order());
    }

    [Fact]
    public void TheQuoteMustCoverTheMatchedSpanSoTheCitedWordsAreInsideTheQuotedWords()
    {
        var narrow = Revision() with { StatementQuote = RelationshipFixtures.Span("introduced H.R.") };

        Assert.Throws<ArgumentException>(narrow.ValidateShape);
    }

    [Fact]
    public void TheQuoteAndTheMatchedSpanMustBelongToOneExtraction()
    {
        var other = Revision() with { StatementQuote = RelationshipFixtures.Span(RelationshipFixtures.BillSentence) with { ExtractionId = new string('d', 64) } };

        Assert.Throws<ArgumentException>(other.ValidateShape);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Two lines.\nHere.")]
    public void TheReasonIsOneNonEmptyLine(string reason) =>
        Assert.Throws<ArgumentException>(() => (Revision() with { Reason = reason }).ValidateShape());

    [Fact]
    public void AStatementCitationMustNameAStatementAndATargetCitationMustNameAnActionOrAVote()
    {
        Assert.Throws<ArgumentException>(() => (Revision() with { Statement = Revision().Target }).ValidateShape());
        Assert.Throws<ArgumentException>(() => (Revision() with { Target = Revision().Statement }).ValidateShape());
    }

    [Fact]
    public void AnAnalysisRevisionFollowsTheCurrentRevisionOfTheSameRecord()
    {
        var first = Revision();

        (first with { RevisionNumber = 2 }).ValidateAsAnalysisRevision(first);

        Assert.Throws<ArgumentException>(() => (first with { RevisionNumber = 3 }).ValidateAsAnalysisRevision(first));
        Assert.Throws<ArgumentException>(() => (first with { RevisionNumber = 2, RecordId = new string('e', 64) }).ValidateAsAnalysisRevision(first));
        Assert.Throws<ArgumentException>(() => (first with { AuthorSubject = "auth0|editor" }).ValidateAsAnalysisRevision(null));
    }
}
