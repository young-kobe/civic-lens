using CivicLens.Application.Relationships;
using CivicLens.Core.Actions;
using CivicLens.Core.Relationships;
using CivicLens.Tests.Fixtures;
using static CivicLens.Tests.Fixtures.RelationshipFixtures;

namespace CivicLens.Tests.Application.Relationships;

public sealed class RelationshipPairFinderTests
{
    [Fact]
    public void ABillNumberTakesTheCongressOfTheStatementDateAndMatchesTheActionOfThatCongress()
    {
        var pairs = RelationshipPairFinder.Find([Statement()], [Action(title: "Unrelated Housing Plan")], []);

        var pair = Assert.Single(pairs);
        Assert.Equal((RelationshipMatchBasis.BillNumber, BillWords), (pair.Match.Basis, Article.Substring(pair.Match.Span.Start, pair.Match.Span.Length)));
        Assert.Equal((ExtractionId, "sponsor"), (pair.Match.Span.ExtractionId, pair.OfficialRole));
    }

    [Fact]
    public void TheSameNumberInAnotherCongressIsAnotherBill()
    {
        var pairs = RelationshipPairFinder.Find([Statement()], [Action(title: "Unrelated Housing Plan", congress: 118)], []);

        Assert.Empty(pairs);
    }

    [Fact]
    public void AStatementWithoutADateCannotPlaceABareNumberSoItIsSkippedAndCounted()
    {
        var pairs = RelationshipPairFinder.Find([Statement(dated: false)], [Action(title: "Unrelated Housing Plan")], []);

        Assert.Empty(pairs);
        Assert.Equal(1, RelationshipPairFinder.CountStatementsWithoutCongress([Statement(dated: false)]));
    }

    [Fact]
    public void AStatementWithoutADateStillMatchesByTitleBecauseATitleNeedsNoCongress()
    {
        var pairs = RelationshipPairFinder.Find([Statement(dated: false)], [Action()], []);

        var pair = Assert.Single(pairs);
        Assert.Equal(RelationshipMatchBasis.BillTitle, pair.Match.Basis);
        Assert.Equal(1, RelationshipPairFinder.CountStatementsWithoutCongress([Statement(dated: false)]));
    }

    [Fact]
    public void ADatedStatementIsNeverCountedAsWithoutACongress() =>
        Assert.Equal(0, RelationshipPairFinder.CountStatementsWithoutCongress([Statement()]));

    [Fact]
    public void ADateBefore1935HasNoCongressSoItIsCountedWithTheDatelessStatementsAndNotSilentlyPaired() =>
        Assert.Equal(1, RelationshipPairFinder.CountStatementsWithoutCongress([Statement(new DateOnly(1920, 5, 1))]));

    [Fact]
    public void AStatementWithoutABillNumberIsNotCountedEvenWhenItHasNoDate() =>
        Assert.Equal(0, RelationshipPairFinder.CountStatementsWithoutCongress([Statement(dated: false, passages: [TitleSentence])]));

    [Theory]
    [InlineData("Lower Rent Act of 2025", 119, true)]
    [InlineData("Lower Rent Act of 2023", 118, false)]
    public void ATitleMatchesOnlyAnActionOfTheCongressOfTheStatementDateSoAnOlderBillWithTheSameNameIsNotPaired(string title, int congress, bool paired) =>
        Assert.Equal(paired, !RelationshipPairFinder.Find([Statement()], [Action(billNumber: 9, title: title, congress: congress) with
            { RecordId = new string('8', 64) }], []).IsEmpty);

    [Fact]
    public void ATitleOfAnotherCongressStillMatchesWhenTheStatementHasNoDateBecauseNothingCanTellTheCongressesApart() =>
        Assert.Single(RelationshipPairFinder.Find([Statement(dated: false)], [Action(billNumber: 9, congress: 118)], []));

    [Theory]
    [InlineData("Lower Rent Act of 2025")]
    [InlineData("The Lower Rent Act")]
    [InlineData("LOWER RENT ACT")]
    public void TheTitleMatchIgnoresCaseALeadingTheAndATrailingYear(string title)
    {
        var pair = Assert.Single(RelationshipPairFinder.Find([Statement()], [Action(billNumber: 9, title: title)], []));

        Assert.Equal(RelationshipMatchBasis.BillTitle, pair.Match.Basis);
        Assert.Equal("Lower Rent Act", Article.Substring(pair.Match.Span.Start, pair.Match.Span.Length), ignoreCase: true);
    }

    [Fact]
    public void ATitleOfTwoWordsIsRejectedEvenWhenTheStatementContainsIt()
    {
        var statement = Statement(article: "Warren: Rent Act is good.", passages: ["Rent Act is good."]);

        Assert.Empty(RelationshipPairFinder.Find([statement], [Action(billNumber: 9, title: "Rent Act")], []));
    }

    [Fact]
    public void ACosponsorWhoWithdrewStillMakesAPairAndTheRoleIsKeptForThePrompt()
    {
        var action = Action(billNumber: 9, role: ActionSponsorshipRole.Cosponsor, original: false, withdrawnOn: new DateOnly(2025, 2, 1));

        var pair = Assert.Single(RelationshipPairFinder.Find([Statement()], [action], []));

        Assert.Equal("cosponsor, withdrawn on 2025-02-01", pair.OfficialRole);
    }

    [Fact]
    public void AnActionThatTheSpeakerDidNotSponsorMakesNoPairBecauseThePairNeedsTheSameOfficial()
    {
        Assert.Empty(RelationshipPairFinder.Find([Statement()], [Action(sponsor: "markey")], []));
    }

    [Fact]
    public void OneBillGivesOnePairEvenWhenTheStatementNamesItByNumberAndByTitle()
    {
        var pair = Assert.Single(RelationshipPairFinder.Find([Statement()], [Action()], []));

        Assert.Equal(RelationshipMatchBasis.BillNumber, pair.Match.Basis);
    }

    [Fact]
    public void EachMatchedBillGivesItsOwnPair()
    {
        const string article = "Warren: I introduced H.R. 1234 and H.R. 77 to cap rent.";
        var statement = Statement(article: article, passages: ["I introduced H.R. 1234 and H.R. 77 to cap rent."]);
        var second = Action(billNumber: 77, title: "Unrelated Housing Plan") with { RecordId = new string('9', 64) };

        var pairs = RelationshipPairFinder.Find([statement], [Action(), second], []);

        Assert.Equal([second.RecordId, ActionId], pairs.Select(pair => pair.Target.RecordId).Order());
    }

    [Fact]
    public void AVoteOnAMentionedBillMakesAPairForTheOfficialWhoCastIt()
    {
        var pair = Assert.Single(RelationshipPairFinder.Find([Statement()], [], [Vote()]));

        Assert.Equal((RelationshipRecordKind.Vote, "voted Yea", RelationshipMatchBasis.BillNumber), (pair.Target.Kind, pair.OfficialRole, pair.Match.Basis));
    }

    [Theory]
    [InlineData(99, "warren")]
    [InlineData(1234, "sanders")]
    public void AVoteOnAnotherBillOrBySomeoneElseMakesNoPair(int billNumber, string person) =>
        Assert.Empty(RelationshipPairFinder.Find([Statement()], [], [Vote(billNumber, person)]));

    [Fact]
    public void ThePairIdIsTheHashOfTheStatementTheKindAndTheTargetAndIgnoresTheMatchBasisAndTheRevisions()
    {
        var number = Assert.Single(RelationshipPairFinder.Find([Statement()], [Action()], []));
        var title = Assert.Single(RelationshipPairFinder.Find([Statement(dated: false)], [Action(revision: 4)], []));

        Assert.Equal(number.PairId, title.PairId);
        Assert.Equal(RelationshipPair.IdOf(StatementId, RelationshipRecordKind.Action, ActionId), number.PairId);
        Assert.Matches("^[0-9a-f]{64}$", number.PairId);
    }

    [Fact]
    public void ThePairIdIsPinnedSoAStoredQueueKeyNeverChangesWithoutAVersionBump() =>
        Assert.Equal("2f9dd634c69e982374fd4d789db078cc2b2e0b5547bf4b5be075ca8f2d707e41", RelationshipPair.IdOf(new string('1', 64), RelationshipRecordKind.Action, new string('a', 64)));

    [Fact]
    public void ThePairIdSeparatesTheKindsOfTarget() =>
        Assert.NotEqual(RelationshipPair.IdOf(StatementId, RelationshipRecordKind.Action, ActionId),
            RelationshipPair.IdOf(StatementId, RelationshipRecordKind.Vote, ActionId));

    [Fact]
    public void TheFingerprintChangesWithTheStatementRevisionTheMatchedWordsTheFactsAndTheRoleOfThePairedOfficial()
    {
        var baseline = RelationshipFingerprint.Compute(ActionPair());

        Assert.NotEqual(baseline, RelationshipFingerprint.Compute(ActionPair(Statement(revision: 2))));
        Assert.NotEqual(baseline, RelationshipFingerprint.Compute(ActionPair(Statement(dated: false))));
        Assert.NotEqual(baseline, RelationshipFingerprint.Compute(ActionPair(action: Action(title: "Lower Rent Act"))));
        Assert.NotEqual(baseline, RelationshipFingerprint.Compute(ActionPair(action: Action(withdrawnOn: new DateOnly(2025, 2, 1)))));
        Assert.Equal(baseline, RelationshipFingerprint.Compute(ActionPair()));
    }

    [Fact]
    public void TheFingerprintChangesWithThePairedOfficialBecauseThePromptNamesThem()
    {
        var statement = Statement();
        var first = ActionPair(statement, Action(sponsor: "warren"));
        var second = first with { OfficialId = "markey" };

        Assert.NotEqual(RelationshipFingerprint.Compute(first), RelationshipFingerprint.Compute(second));
    }

    [Fact]
    public void TheFingerprintIgnoresTheTargetRevisionNumberAndTheCastsOfOtherMembers()
    {
        Assert.Equal(RelationshipFingerprint.Compute(ActionPair()), RelationshipFingerprint.Compute(ActionPair(action: Action(revision: 2))));
        Assert.NotEqual(RelationshipFingerprint.Compute(VotePair()), RelationshipFingerprint.Compute(VotePair(vote: Vote(cast: "Nay"))));
        Assert.Equal(RelationshipFingerprint.Compute(VotePair()), RelationshipFingerprint.Compute(VotePair(vote: Vote(revision: 2))));
    }

    [Fact]
    public void TheFingerprintIsPinnedSoAStoredSettledInputKeepsItsMeaningUntilTheVersionIsBumped() =>
        Assert.Equal("b063f692caf5c8147cca1f79b814ee641ca10c069b912319edfa80791407badd", RelationshipFingerprint.Compute(ActionPair()));
}
