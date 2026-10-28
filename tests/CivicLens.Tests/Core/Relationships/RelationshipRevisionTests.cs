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
        RelationshipFixtures.Span(RelationshipFixtures.BillSentence), "F2", "warren");

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

    [Fact]
    public void APersonEditChangesOnlyTheTypeTheReasonTheQuoteAndTheFact()
    {
        var first = Revision();
        var edit = first with
        {
            RevisionNumber = 2,
            AuthorSubject = "auth0|reviewer",
            Type = RelationshipType.ClaimsCreditFor,
            Reason = "Credit.",
            StatementQuote = RelationshipFixtures.Span(RelationshipFixtures.BillWords),
            TargetFactId = "F1"
        };

        edit.ValidatePersonEditOf(first);

        foreach (var forged in new[]
        {
            edit with { Statement = first.Statement with { RevisionNumber = 9 } },
            edit with { Target = first.Target with { RecordId = new string('9', 64) } },
            edit with { Match = first.Match with { Basis = RelationshipMatchBasis.BillTitle } },
            edit with { OfficialId = "other" },
            edit with { RecordId = new string('e', 64) },
            edit with { AuthorSubject = ReviewAuthor.AnalysisSubject },
            edit with { RevisionNumber = 3 }
        })
            Assert.Throws<ArgumentException>(() => forged.ValidatePersonEditOf(first));
    }

    [Fact]
    public void ARevisionNamesTheMatchedOfficialWithinTheLimit()
    {
        Revision().ValidateShape();

        Assert.Throws<ArgumentException>((Revision() with { OfficialId = " " }).ValidateShape);
        Assert.Throws<ArgumentException>((Revision() with { OfficialId = new string('o', RelationshipRevision.MaximumOfficialIdLength + 1) }).ValidateShape);
    }

    [Fact]
    public void ASpanCoversAnotherSpanOnlyInTheSameExtractionAndInsideItsOwnRange()
    {
        var outer = new SourceSpan(RelationshipFixtures.ExtractionId, 10, 20);

        Assert.True(outer.Covers(outer with { Start = 15, Length = 5 }));
        Assert.False(outer.Covers(outer with { Start = 5, Length = 10 }));
        Assert.False(outer.Covers(outer with { Start = 25, Length = 10 }));
        Assert.False(outer.Covers(outer with { ExtractionId = new string('9', 64), Start = 15, Length = 5 }));
    }
}
