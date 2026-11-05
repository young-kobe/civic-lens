using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.Publication;

public sealed class ChamberAgreementTests
{
    private static readonly DateTimeOffset Published = new(2026, 5, 11, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void OnlyEntriesWithAnAlignmentCountSoAProceduralVoteNeverEntersTheTotal()
    {
        var release = ReleaseOf(Vote(1, PublishedVoteKind.Passage, PublishedVoteAlignment.PartiesAgree), Vote(2, PublishedVoteKind.Rule, null), Vote(3, PublishedVoteKind.Passage, null));

        var agreement = ChamberAgreement.From(release, PublishedChamber.House, 119)!;

        Assert.Equal(1, agreement.Total);
        Assert.Equal([PublishedVoteKind.Passage], agreement.Kinds.Select(kind => kind.Kind));
    }

    [Fact]
    public void AgreedPlusSplitPlusMixedEqualsTheTotalAndEachGroupHoldsItsOwnAlignment()
    {
        var release = ReleaseOf(Vote(1, PublishedVoteKind.Passage, PublishedVoteAlignment.PartiesAgree), Vote(2, PublishedVoteKind.Passage, PublishedVoteAlignment.PartiesAgree),
            Vote(3, PublishedVoteKind.Amendment, PublishedVoteAlignment.Split), Vote(4, PublishedVoteKind.Passage, PublishedVoteAlignment.Mixed));

        var agreement = ChamberAgreement.From(release, PublishedChamber.House, 119)!;

        Assert.Equal((2, 1, 1), (agreement.Agreed, agreement.Split, agreement.Mixed));
        Assert.Equal(4, agreement.Total);
    }

    [Fact]
    public void OnlyTheBoardCongressAndChamberCount()
    {
        var release = ReleaseOf(Vote(1, PublishedVoteKind.Passage, PublishedVoteAlignment.PartiesAgree), Vote(2, PublishedVoteKind.Passage, PublishedVoteAlignment.PartiesAgree) with { Congress = 118 },
            Vote(3, PublishedVoteKind.Passage, PublishedVoteAlignment.PartiesAgree) with { Chamber = PublishedChamber.Senate });

        Assert.Equal(1, ChamberAgreement.From(release, PublishedChamber.House, 119)!.Total);
        Assert.Equal(1, ChamberAgreement.From(release, PublishedChamber.House, 118)!.Total);
        Assert.Equal(1, ChamberAgreement.From(release, PublishedChamber.Senate, 119)!.Total);
    }

    [Fact]
    public void EachKindRowCountsItsOwnVotesAndTheRowsAddUpToTheTotal()
    {
        var release = ReleaseOf(Vote(1, PublishedVoteKind.Nomination, PublishedVoteAlignment.PartiesAgree), Vote(2, PublishedVoteKind.Passage, PublishedVoteAlignment.Split),
            Vote(3, PublishedVoteKind.Passage, PublishedVoteAlignment.PartiesAgree));

        var agreement = ChamberAgreement.From(release, PublishedChamber.House, 119)!;

        Assert.Equal([(PublishedVoteKind.Passage, 1, 2), (PublishedVoteKind.Nomination, 1, 1)],
            agreement.Kinds.Select(kind => (kind.Kind, kind.Agreed, kind.Total)).OrderBy(row => row.Kind == PublishedVoteKind.Nomination));
        Assert.Equal(agreement.Total, agreement.Kinds.Sum(kind => kind.Total));
        Assert.Equal(1, agreement.Nominations);
    }

    [Fact]
    public void TheCountReadsTheEntryAlignmentSoAYeaNayGapNeverChangesIt()
    {
        var release = ReleaseOf(Vote(1, PublishedVoteKind.Passage, PublishedVoteAlignment.Split) with { YeaCount = 400, NayCount = 10 });

        Assert.Equal(1, ChamberAgreement.From(release, PublishedChamber.House, 119)!.Split);
    }

    [Fact]
    public void OneVoteEntryWithNoCongressHidesTheCountEvenWhenItBelongsToAnotherChamber()
    {
        var carried = Vote(9, PublishedVoteKind.Passage, null) with { VoteKind = null, Congress = null, Chamber = PublishedChamber.Senate };
        var release = ReleaseOf(Vote(1, PublishedVoteKind.Passage, PublishedVoteAlignment.PartiesAgree), carried);

        Assert.Null(ChamberAgreement.From(release, PublishedChamber.House, 119));
    }

    [Fact]
    public void ABoardWithNoCongressHasNoCountAndAChamberWithNoAlignedVoteHasAnEmptyOne()
    {
        var release = ReleaseOf(Vote(1, PublishedVoteKind.Rule, null));

        Assert.Null(ChamberAgreement.From(release, PublishedChamber.House, null));
        Assert.Equal(0, ChamberAgreement.From(release, PublishedChamber.House, 119)!.Total);
    }

    private static PublicationRelease ReleaseOf(params PublishedRecordEntry[] entries) => new()
    {
        SchemaVersion = PublicationProtocol.SchemaVersion,
        ReleaseNumber = 3,
        PublishedAtUtc = Published,
        Withdrawals = [],
        Records = entries
    };

    private static PublishedRecordEntry Vote(int number, PublishedVoteKind kind, PublishedVoteAlignment? alignment) => new()
    {
        Kind = PublishedRecordKind.Vote,
        RecordId = number.ToString("x").PadLeft(64, '0'),
        RevisionNumber = 1,
        Headline = $"Headline {number}",
        FirstPublishedAtUtc = Published,
        Officials = [],
        IssueIds = [],
        PolicyAreaIds = [],
        Chamber = PublishedChamber.House,
        VoteKind = kind,
        Congress = 119,
        Alignment = alignment
    };
}
