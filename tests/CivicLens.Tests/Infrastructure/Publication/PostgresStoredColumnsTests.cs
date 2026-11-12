using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using CivicLens.Application.Publication;
using CivicLens.Application.Roster;
using CivicLens.Core.Votes;
using CivicLens.Host.Publication;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Application.Publication;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Publication;

/// <summary>
/// Each test writes rows through the real writer of the release commit or the backfill, then compares the stored value with the C# rule on the same input.
/// The release builder builds the vote in <see cref="PublicationScenario"/>, so the rule inputs are the roster and the member lines that the builder reads.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresStoredColumnsTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string Second = "R1";
    private const string SecondLine = "Member: R1, A, R, AL, No";
    private const string RepublicanNay = "Party total R: Yea 0, Nay 1, Present 0, Not voting 0";

    private string connectionString = null!;
    private PostgresPublicationStore publications = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        publications = new PostgresPublicationStore(new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options), TestCodeVersion.Current);
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task PartyTallyMigrationUsesTheConfiguredPrimaryAndForeignKeyNames()
    {
        await using var db = new CollectionAttemptDbContext(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
        var entity = db.Model.FindEntityType("CivicLens.Infrastructure.Votes.Persistence.VoteRevisionPartyTallyRow")!;
        var names = new[] { entity.FindPrimaryKey()!.GetName()!, Assert.Single(entity.GetForeignKeys()).GetConstraintName()! };
        Assert.Equal(names.Order(StringComparer.Ordinal),
            await ReadAsync("SELECT conname FROM pg_constraint WHERE conrelid = 'vote_revision_party_tallies'::regclass AND contype IN ('f', 'p') ORDER BY conname"));
    }

    [Fact]
    public async Task TallyForeignKeyRejectsAMissingRevisionAfterSparseLegacyFixtureSetup()
    {
        var published = await PublishVoteAsync([]);
        await SeedAsync(published, 1);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync($"INSERT INTO vote_revision_party_tallies (record_id, revision_number, party, yea, nay, present, not_voting, other) VALUES ('{published.Vote.RecordId}', 99, 'Unknown', 1, 0, 0, 0, 0)"));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
        await using var db = new CollectionAttemptDbContext(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
        var entity = db.Model.FindEntityType("CivicLens.Infrastructure.Votes.Persistence.VoteRevisionPartyTallyRow")!;
        Assert.Equal(Assert.Single(entity.GetForeignKeys()).GetConstraintName(), exception.ConstraintName);
    }

    // The roster holds two term rows for the switch. Real legislators data puts an in-term switch inside one term, which the parser does not split. See the roster Limits.
    [Theory]
    [InlineData("Democrat", "2025-03-31", "Republican", "Republican")]
    [InlineData("Republican", "2025-06-30", "Democrat", "Republican")]
    public async Task ATermSplitAtAPartyChangeStoresThePartyOfTheTermThatCoversTheVoteDayBecauseTheReleaseUsesThatTerm(string before, string lastDay,
        string after, string expected)
    {
        var next = DateOnly.Parse(lastDay).AddDays(1).ToString("yyyy-MM-dd");
        RosterMember[] roster = [Member("M000001", Term(0, "2025-01-03", lastDay, before), Term(1, next, "2027-01-03", after)), Member(Second, Term(0, "2025-01-03", "2027-01-03", "Republican"))];
        var published = await PublishVoteAsync(roster);

        await WriteThroughCommitAsync(published);

        Assert.Equal([expected], await StoredPartiesAsync(published));
        Assert.Equal(ExpectedParties(published.Vote), await StoredPartiesAsync(published));
    }

    [Fact]
    public async Task AMemberWhoIsNotOnTheRosterStoresThePartyOfTheSourceLetterBecauseTheReleaseUsesTheLetterThen()
    {
        RosterMember[] roster = [Member(Second, Term(0, "2025-01-03", "2027-01-03", "Republican"))];
        var published = await PublishVoteAsync(roster, trackedSourceId: "X9");

        await WriteThroughCommitAsync(published);

        Assert.Equal(["Democrat"], await StoredPartiesAsync(published));
        Assert.Equal(ExpectedParties(published.Vote), await StoredPartiesAsync(published));
    }

    [Fact]
    public async Task AVoteWithOneMajorPartyStoresNoAlignmentAndNoMajorityForTheMissingPartyBecauseTheReleaseGivesNull()
    {
        var published = await PublishVoteAsync([Member("M000001", Term(0, "2025-01-03", "2027-01-03", "Democrat"))], otherLines: [], totals: [PublicationScenario.DefaultPartyTotal]);

        await WriteThroughCommitAsync(published);

        Assert.Equal("|Yea|", await StoredRevisionColumnsAsync(published));
        Assert.Null(published.Vote.AlignmentOfParties());
        Assert.Null(published.Vote.MajorityOf(PublishedPartyMajority.Republican));
    }

    [Fact]
    public async Task APartiesSplitVoteStoresTheAlignmentAndTheMajorityOfEachMajorPartyAsTheRuleGivesThem()
    {
        RosterMember[] roster = [Member("M000001", Term(0, "2025-01-03", "2027-01-03", "Democrat")), Member(Second, Term(0, "2025-01-03", "2027-01-03", "Republican"))];
        var published = await PublishVoteAsync(roster);

        await WriteThroughCommitAsync(published);

        Assert.Equal("Split|Yea|Nay", await StoredRevisionColumnsAsync(published));
        Assert.Equal(ExpectedRevisionColumns(published.Vote), await StoredRevisionColumnsAsync(published));
    }

    [Fact]
    public async Task PartyTalliesPersistEveryBucketOncePerRevisionAndRejectConflictingReplay()
    {
        var published = await PublishVoteAsync([]);
        await SeedAsync(published, 1, throughRevision: 2);
        var tallies = ImmutableArray.Create(
            new PublishedPartyTally { Party = "Independent", Yea = 1, Nay = 2, Present = 3, NotVoting = 4, Other = 5 },
            new PublishedPartyTally { Party = "Unknown", Yea = 6, Nay = 7, Present = 8, NotVoting = 9, Other = 10 });
        var columns = VoteStoredColumns.From(published.Vote) with { PartyTallies = tallies };

        await publications.WriteVoteColumnsAsync([columns], default);
        await publications.WriteVoteColumnsAsync([columns], default);

        Assert.Equal(["Independent|1|2|3|4|5", "Unknown|6|7|8|9|10"], await ReadAsync("SELECT party || '|' || yea || '|' || nay || '|' || present || '|' || not_voting || '|' || other FROM vote_revision_party_tallies WHERE revision_number = 1 ORDER BY party"));
        Assert.Equal(["2", ""], await ReadAsync("SELECT coalesce(party_tally_count::text, '') FROM vote_revisions ORDER BY revision_number"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => publications.WriteVoteColumnsAsync([columns with { PartyTallies = [tallies[0] with { Other = 6 }, tallies[1]] }], default));
        Assert.Equal(2, await ScalarIntAsync("SELECT count(*) FROM vote_revision_party_tallies"));
    }

    [Fact]
    public async Task UnavailableTalliesAreFrozenAndMalformedGroupsWriteNothing()
    {
        var published = await PublishVoteAsync([]);
        await SeedAsync(published, 1, throughRevision: 2);
        var unavailable = VoteStoredColumns.From(published.Vote) with { PartyTallies = default };
        await publications.WriteVoteColumnsAsync([unavailable], default);

        var invalidEmpty = unavailable with { RevisionNumber = 2, PartyTallies = [] };
        var invalidNull = unavailable with { RevisionNumber = 2, PartyTallies = [null!] };
        await Assert.ThrowsAsync<InvalidDataException>(() => publications.WriteVoteColumnsAsync([invalidEmpty], default));
        await Assert.ThrowsAsync<InvalidDataException>(() => publications.WriteVoteColumnsAsync([invalidNull], default));

        Assert.Equal(["0", ""], await ReadAsync("SELECT coalesce(party_tally_count::text, '') FROM vote_revisions ORDER BY revision_number"));
        Assert.Equal(0, await ScalarIntAsync("SELECT count(*) FROM vote_revision_party_tallies"));
    }

    [Fact]
    public async Task BackfillSelectsAnAlreadyWrittenRevisionWhoseTallyMarkerIsStillUnresolved()
    {
        var published = await PublishVoteAsync([Member("M000001", Term(0, "2025-01-03", "2027-01-03", "Democrat"))], otherLines: [], totals: [PublicationScenario.DefaultPartyTotal]);
        await SeedAsync(published, published.Vote.RevisionNumber, inRelease: true);
        await ExecuteAsync("UPDATE vote_revisions SET columns_written = true WHERE revision_number = 1");

        var result = await new BackfillStoredColumns(publications, published.Scenario.Releases).ExecuteAsync(PublicationScenario.Owner);

        Assert.Equal(1, result.VoteRevisionsExamined);
        Assert.Equal(published.Vote.PartyTallies!.Length.ToString(), (await ReadAsync("SELECT party_tally_count::text FROM vote_revisions WHERE revision_number = 1"))[0]);
    }

    [Fact]
    public async Task TallyWriteRejectsPreexistingRowsForUnresolvedMarkersAndPartialRowsForFrozenMarkers()
    {
        var published = await PublishVoteAsync([]);
        await SeedAsync(published, 1, throughRevision: 2);
        var first = VoteStoredColumns.From(published.Vote) with { RevisionNumber = 1 };
        var second = VoteStoredColumns.From(published.Vote) with { RevisionNumber = 2 };
        await ExecuteAsync($"INSERT INTO vote_revision_party_tallies (record_id, revision_number, party, yea, nay, present, not_voting, other) VALUES ('{published.Vote.RecordId}', 1, 'Unknown', 1, 0, 0, 0, 0)");
        await Assert.ThrowsAsync<InvalidOperationException>(() => publications.WriteVoteColumnsAsync([first], default));

        await ExecuteAsync("UPDATE vote_revisions SET party_tally_count = 2 WHERE revision_number = 2");
        await ExecuteAsync($"INSERT INTO vote_revision_party_tallies (record_id, revision_number, party, yea, nay, present, not_voting, other) VALUES ('{published.Vote.RecordId}', 2, 'Unknown', 1, 0, 0, 0, 0)");
        await Assert.ThrowsAsync<InvalidOperationException>(() => publications.WriteVoteColumnsAsync([second], default));

        Assert.Equal(["", "2"], await ReadAsync("SELECT coalesce(party_tally_count::text, '') FROM vote_revisions ORDER BY revision_number"));
        Assert.Equal(2, await ScalarIntAsync("SELECT count(*) FROM vote_revision_party_tallies"));
    }

    [Fact]
    public async Task AWriteNeverChangesThePartiesOfAListThatHoldsThemAndStillWritesTheColumnsOfItsRevisionBecauseThePartyOfAVoteDayIsFixed()
    {
        RosterMember[] roster = [Member("M000001", Term(0, "2025-01-03", "2027-01-03", "Democrat")), Member(Second, Term(0, "2025-01-03", "2027-01-03", "Republican"))];
        var published = await PublishVoteAsync(roster);
        await SeedAsync(published, published.Vote.RevisionNumber, throughRevision: 2);
        var columns = VoteStoredColumns.From(published.Vote);
        await publications.WriteVoteColumnsAsync([columns], default);

        await publications.WriteVoteColumnsAsync([columns with { Alignment = PublishedVoteAlignment.Mixed, DemocratMajority = "Nay" }], default);
        await publications.WriteVoteColumnsAsync([columns with { RevisionNumber = 2, Alignment = PublishedVoteAlignment.PartiesAgree, MemberParties = ["Republican"] }], default);

        Assert.Equal(["Democrat"], await StoredPartiesAsync(published));
        Assert.Equal(["Split|Yea|Nay", "PartiesAgree|Yea|Nay"], await ReadAsync("SELECT coalesce(alignment, '') || '|' || coalesce(democrat_majority, '') || '|' || coalesce(republican_majority, '') FROM vote_revisions ORDER BY revision_number"));
        Assert.Equal(["true", "true"], await ReadAsync("SELECT columns_written::text FROM vote_revisions ORDER BY revision_number"));
    }

    [Fact]
    public async Task AWriteThatPinsTwoDifferentPartySetsOnOneListFailsAndWritesNothingBecauseTheTwoWouldRaceInOneUpdate()
    {
        RosterMember[] roster = [Member("M000001", Term(0, "2025-01-03", "2027-01-03", "Democrat")), Member(Second, Term(0, "2025-01-03", "2027-01-03", "Republican"))];
        var published = await PublishVoteAsync(roster);
        await SeedAsync(published, published.Vote.RevisionNumber, throughRevision: 2);
        var columns = VoteStoredColumns.From(published.Vote);

        await Assert.ThrowsAsync<InvalidOperationException>(() => publications.WriteVoteColumnsAsync([columns, columns with { RevisionNumber = 2, MemberParties = ["Republican"] }], default));

        Assert.Equal([""], await StoredPartiesAsync(published));
        Assert.Equal(["false", "false"], await ReadAsync("SELECT columns_written::text FROM vote_revisions ORDER BY revision_number"));
    }

    [Fact]
    public async Task TheCommitWritesTheVoteColumnsInTheTransactionOfTheReleaseSoAFailureAfterThemLeavesNoColumnsBehind()
    {
        var published = await PublishVoteAsync([Member("M000001", Term(0, "2025-01-03", "2027-01-03", "Democrat")), Member(Second, Term(0, "2025-01-03", "2027-01-03", "Republican"))]);
        await SeedAsync(published, published.Vote.RevisionNumber);
        var withdrawal = new PublishedWithdrawal { Kind = PublishedRecordKind.Vote, RecordId = new string('f', 64), WithdrawnRevisionNumber = 1, FirstPublishedAtUtc = DateTimeOffset.UnixEpoch, WithdrawnAtUtc = DateTimeOffset.UnixEpoch, Note = "Withdrawn.", Corrections = [] };
        var failing = new PublicationCommit(1, null, $"000001-{Guid.NewGuid():N}", [], []) { Withdrawals = [withdrawal], Names = ReleaseNameFixtures.None, VoteColumns = published.Commit.VoteColumns };
        var good = new PublicationCommit(1, null, $"000001-{Guid.NewGuid():N}", [], []) { Withdrawals = [], Names = ReleaseNameFixtures.None, VoteColumns = published.Commit.VoteColumns };

        await Assert.ThrowsAnyAsync<Exception>(() => publications.CommitAsync("auth0|owner", failing, "key", new string('6', 64), default));
        var afterFailure = (await ReadAsync("SELECT columns_written::text || '|' || coalesce(party_tally_count::text, '') || '|' || (SELECT count(*) FROM vote_revision_party_tallies) || '|' || (SELECT count(*) FROM publication_releases) FROM vote_revisions"))[0];
        await publications.CommitAsync("auth0|owner", good, "key2", new string('7', 64), default);

        Assert.Equal("false||0|0", afterFailure);
        Assert.Equal($"true|{published.Vote.PartyTallies!.Length}|{published.Vote.PartyTallies.Length}|1", (await ReadAsync("SELECT columns_written::text || '|' || party_tally_count || '|' || (SELECT count(*) FROM vote_revision_party_tallies) || '|' || (SELECT count(*) FROM publication_releases) FROM vote_revisions"))[0]);
    }

    [Fact]
    public async Task AnOfficialWithAPhotoStoresItsPathAndAnOfficialWithNoPhotoStoresNullBecauseTheReleaseFileHoldsTheSame()
    {
        var photo = new PublishedPhoto { Path = PublicationProtocol.PhotoPath(new string('a', 64)), Credit = "Credit", SourceUrl = "https://example.gov/a.jpg", Width = 200, Height = 250 };
        PublishedOfficialProfile[] officials = [Official("with-photo", photo), Official("no-photo", null)];

        await publications.CommitAsync("auth0|owner", NamesCommit(officials), "key", new string('1', 64), default);

        Assert.Equal(["no-photo|", "with-photo|" + photo.Path], await ReadAsync("SELECT official_id || '|' || coalesce(photo_path, '') FROM release_officials ORDER BY official_id"));
        Assert.Equal(officials.OrderBy(official => official.Id, StringComparer.Ordinal).Select(official => official.Id + "|" + official.Photo?.Path), await ReadAsync("SELECT official_id || '|' || coalesce(photo_path, '') FROM release_officials ORDER BY official_id"));
    }

    [Fact]
    public async Task AnEntryStoresTheCongressOfItsRecordDayAndAnEntryWithNoEventDateUsesTheFirstPublicationDay()
    {
        var first = new DateTimeOffset(2025, 2, 1, 0, 0, 0, TimeSpan.Zero);
        PublishedRecordEntry[] entries =
        [
            PublishedEntryFixtures.Action(new string('a', 64), first) with { EventDate = new DateOnly(2024, 12, 31) },
            PublishedEntryFixtures.Action(new string('b', 64), first) with { EventDate = new DateOnly(2025, 1, 3) },
            PublishedEntryFixtures.Action(new string('c', 64), first) with { EventDate = null },
            PublishedEntryFixtures.Action(new string('d', 64), first) with { Kind = PublishedRecordKind.Statement, BillTitle = null, ApprovedBy = PublishedApprover.Code }
        ];

        await ExecuteAsync("ALTER TABLE published_entries DISABLE TRIGGER ALL");
        await publications.CommitAsync("auth0|owner", EntriesCommit(entries), "key", new string('2', 64), default);

        Assert.Equal(["a|118", "b|119", "c|119", "d|119"], await ReadAsync("SELECT left(record_id, 1) || '|' || congress FROM published_entries ORDER BY record_id"));
        Assert.Equal(entries.Select(entry => entry.RecordId[..1] + "|" + ReleaseRecordOrder.CongressOf(entry)), await ReadAsync("SELECT left(record_id, 1) || '|' || congress FROM published_entries ORDER BY record_id"));
        Assert.Equal(["a|", "b|", "c|", "d|Code"], await ReadAsync("SELECT left(record_id, 1) || '|' || coalesce(approved_by, '') FROM published_entries ORDER BY record_id"));
    }

    [Fact]
    public async Task TheBackfillFillsTheVoteColumnsFromTheKeptRecordFileWithTheTermOfTheVoteDayAndASecondRunChangesNothing()
    {
        RosterMember[] roster = [Member("M000001", Term(0, "2025-01-03", "2025-03-31", "Democrat"), Term(1, "2025-04-01", "2027-01-03", "Republican")), Member(Second, Term(0, "2025-01-03", "2027-01-03", "Democrat"))];
        var published = await PublishVoteAsync(roster);
        await SeedAsync(published, published.Vote.RevisionNumber, inRelease: true);
        var backfill = new BackfillStoredColumns(publications, published.Scenario.Releases);

        var first = await backfill.ExecuteAsync(PublicationScenario.Owner);
        var stored = await StoredPartiesAsync(published);
        var columns = await StoredRevisionColumnsAsync(published);
        var second = await backfill.ExecuteAsync(PublicationScenario.Owner);

        Assert.Equal(1, first.VoteRevisionsExamined);
        Assert.Equal(1, first.VoteRevisionsWritten);
        Assert.Equal(["Republican"], stored);
        Assert.Equal(ExpectedParties(published.Vote), stored);
        Assert.Equal(ExpectedRevisionColumns(published.Vote), columns);
        var expectedTallies = published.Vote.PartyTallies!.OrderBy(tally => tally.Party, StringComparer.Ordinal)
            .Select(tally => $"{tally.Party}|{tally.Yea}|{tally.Nay}|{tally.Present}|{tally.NotVoting}|{tally.Other}");
        Assert.Equal(expectedTallies, await ReadAsync("SELECT party || '|' || yea || '|' || nay || '|' || present || '|' || not_voting || '|' || other FROM vote_revision_party_tallies ORDER BY party"));
        Assert.Equal(stored, await StoredPartiesAsync(published));
        Assert.Equal(columns, await StoredRevisionColumnsAsync(published));
        Assert.Equal(0, second.VoteRevisionsExamined);
        Assert.Equal(0, second.OpenVoteRevisionsWithoutKeptFile);
    }

    [Fact]
    public async Task TheBackfillReportsAVoteRevisionThatNoKeptFolderHoldsAndLeavesItUnwritten()
    {
        var published = await PublishVoteAsync([Member("M000001", Term(0, "2025-01-03", "2027-01-03", "Democrat"))], otherLines: [], totals: [PublicationScenario.DefaultPartyTotal]);
        await SeedAsync(published, published.Vote.RevisionNumber, inRelease: true);
        published.Scenario.Releases.Directories.Clear();

        var result = await new BackfillStoredColumns(publications, published.Scenario.Releases).ExecuteAsync(PublicationScenario.Owner);

        Assert.Equal((1, 0, 0), (result.OpenVoteRevisionsWithoutKeptFile, result.VoteRevisionsWritten, result.ClosedVoteRevisionsWithoutKeptFile));
        Assert.Equal([""], await StoredPartiesAsync(published));
        Assert.Equal(["false"], await ReadAsync("SELECT columns_written::text FROM vote_revisions"));
        Assert.Equal(1, await ExitOfBackfillAsync(published.Scenario.Releases));
    }

    [Fact]
    public async Task TwoRevisionsThatShareAListGetTheirOwnColumnsFromTheirOwnFilesAndTheListKeepsThePartiesOfTheEarlierRevisionInOneCall()
    {
        var published = await PublishVoteAsync([Member("M000001", Term(0, "2025-01-03", "2027-01-03", "Democrat")), Member(Second, Term(0, "2025-01-03", "2027-01-03", "Republican"))]);
        await SeedAsync(published, 1, throughRevision: 2);
        var first = FileOf(published, 1, "Democrat");
        var second = FileOf(published, 2, "Republican");
        await SeedReleasesAsync(published, (1, first, 1), (2, second, null));

        var result = await new BackfillStoredColumns(publications, published.Scenario.Releases).ExecuteAsync(PublicationScenario.Owner);

        Assert.Equal((2, 2), (result.VoteRevisionsExamined, result.VoteRevisionsWritten));
        Assert.Equal(["Democrat"], await ReadAsync("SELECT coalesce(party, '') FROM vote_member_list_entries"));
        Assert.Equal([ExpectedRevisionColumns(first), ExpectedRevisionColumns(second)], await ReadAsync(
            "SELECT coalesce(alignment, '') || '|' || coalesce(democrat_majority, '') || '|' || coalesce(republican_majority, '') FROM vote_revisions ORDER BY revision_number"));
        Assert.NotEqual(ExpectedRevisionColumns(first), ExpectedRevisionColumns(second));
    }

    [Fact]
    public async Task TheListTakesThePartiesOfTheEarliestRevisionThatHasAKeptFileAndAClosedRevisionWithNoFileIsReportedButDoesNotFailTheCommand()
    {
        var published = await PublishVoteAsync([Member("M000001", Term(0, "2025-01-03", "2027-01-03", "Democrat")), Member(Second, Term(0, "2025-01-03", "2027-01-03", "Republican"))]);
        await SeedAsync(published, 1, throughRevision: 2);
        var second = FileOf(published, 2, "Republican");
        await SeedReleasesAsync(published, (1, null, 1), (2, second, null));
        var releases = published.Scenario.Releases;

        var result = await new BackfillStoredColumns(publications, releases).ExecuteAsync(PublicationScenario.Owner);
        var exit = await ExitOfBackfillAsync(releases);

        Assert.Equal((2, 1, 0, 1), (result.VoteRevisionsExamined, result.VoteRevisionsWritten, result.OpenVoteRevisionsWithoutKeptFile, result.ClosedVoteRevisionsWithoutKeptFile));
        Assert.Equal(["Republican"], await ReadAsync("SELECT coalesce(party, '') FROM vote_member_list_entries"));
        Assert.Equal(["false", "true"], await ReadAsync("SELECT columns_written::text FROM vote_revisions ORDER BY revision_number"));
        Assert.Equal(0, exit);
    }

    [Fact]
    public async Task OneSharedListThatCrossesABatchOfTwentyFivePinsItsPartiesInTheFirstBatchOnlyBecauseTheBatchesShareOneSetOfPinnedLists()
    {
        const int revisions = 27;
        var published = await PublishVoteAsync([Member("M000001", Term(0, "2025-01-03", "2027-01-03", "Democrat")), Member(Second, Term(0, "2025-01-03", "2027-01-03", "Republican"))]);
        await SeedAsync(published, 1, throughRevision: revisions);
        await SeedReleasesAsync(published, [.. Enumerable.Range(1, revisions).Select(revision => (revision, (PublishedVote?)FileOf(published, revision, revision == 1 ? "Democrat" : "Republican"),
            revision == revisions ? (int?)null : revision))]);
        var writes = new List<VoteStoredColumns[]>();
        var recording = DispatchProxy.Create<IPublicationStore, RecordingStore>();
        ((RecordingStore)recording).Start(publications, writes);

        var result = await new BackfillStoredColumns(recording, published.Scenario.Releases).ExecuteAsync(PublicationScenario.Owner);

        Assert.Equal((revisions, revisions), (result.VoteRevisionsExamined, result.VoteRevisionsWritten));
        Assert.Equal([25, 2], writes.Select(write => write.Length));
        Assert.Equal([1], writes.SelectMany(write => write).Where(column => !column.MemberParties.IsEmpty).Select(column => column.RevisionNumber));
        Assert.Equal(["Democrat"], await ReadAsync("SELECT coalesce(party, '') FROM vote_member_list_entries"));
    }

    // Forwards every call to the real store and records the vote column batches that the backfill writes.
    public class RecordingStore : DispatchProxy
    {
        private IPublicationStore inner = null!;
        private List<VoteStoredColumns[]> writes = null!;

        public void Start(IPublicationStore store, List<VoteStoredColumns[]> sink) => (inner, writes) = (store, sink);

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod!.Name == nameof(IPublicationStore.WriteVoteColumnsAsync)) writes.Add([.. (IReadOnlyCollection<VoteStoredColumns>)args![0]!]);
            return targetMethod.Invoke(inner, args);
        }
    }

    [Fact]
    public async Task TheBackfillReadsOnlyTheFoldersOfTheReleaseRangeOfEachRevisionSoAFileOutsideTheRangeDoesNotFillIt()
    {
        var published = await PublishVoteAsync([Member("M000001", Term(0, "2025-01-03", "2027-01-03", "Democrat")), Member(Second, Term(0, "2025-01-03", "2027-01-03", "Republican"))]);
        await SeedAsync(published, 1, throughRevision: 2);
        await SeedReleasesAsync(published, (1, null, 1), (2, FileOf(published, 1, "Democrat"), null));

        var result = await new BackfillStoredColumns(publications, published.Scenario.Releases).ExecuteAsync(PublicationScenario.Owner);

        Assert.Equal((0, 1, 1), (result.VoteRevisionsWritten, result.OpenVoteRevisionsWithoutKeptFile, result.ClosedVoteRevisionsWithoutKeptFile));
        Assert.Equal(["false", "false"], await ReadAsync("SELECT columns_written::text FROM vote_revisions ORDER BY revision_number"));
    }

    [Fact]
    public async Task TheBackfillFillsThePhotoPathFromTheOfficialsFileOfEachReleaseAndLeavesAnOfficialWithNoPhotoNull()
    {
        var photo = new PublishedPhoto { Path = PublicationProtocol.PhotoPath(new string('b', 64)), Credit = "Credit", SourceUrl = "https://example.gov/b.jpg", Width = 200, Height = 250 };
        PublishedOfficialProfile[] officials = [Official("with-photo", photo), Official("no-photo", null)];
        var commit = NamesCommit(officials);
        await publications.CommitAsync("auth0|owner", commit, "key", new string('3', 64), default);
        await ExecuteAsync("UPDATE release_officials SET photo_path = NULL");
        var releases = new FakeReleaseDirectory();
        releases.Directories[commit.DirectoryName] = new() { [PublicationProtocol.OfficialsPath] = JsonSerializer.SerializeToUtf8Bytes(new PublishedOfficials { Officials = officials }, PublicationProtocol.JsonOptions) };
        var backfill = new BackfillStoredColumns(publications, releases);

        var first = await backfill.ExecuteAsync(PublicationScenario.Owner);
        var second = await backfill.ExecuteAsync(PublicationScenario.Owner);

        Assert.Equal(1, first.OfficialPhotoPathsFilled);
        Assert.Equal(0, second.OfficialPhotoPathsFilled);
        Assert.Equal(["no-photo|", "with-photo|" + photo.Path], await ReadAsync("SELECT official_id || '|' || coalesce(photo_path, '') FROM release_officials ORDER BY official_id"));
    }

    [Fact]
    public async Task TheBackfillFillsTheCongressOfEntryRowsInBatchesWithTheRuleOfTheCommitAndASecondRunChangesNothing()
    {
        var first = new DateTimeOffset(2025, 2, 1, 0, 0, 0, TimeSpan.Zero);
        var entries = Enumerable.Range(0, 30).Select(index => PublishedEntryFixtures.Action(new string('a', 62) + index.ToString("00"), first)
            with
        { EventDate = index == 0 ? new DateOnly(1900, 1, 1) : new DateOnly(2024, 12, 31).AddDays(index) }).ToArray();
        await ExecuteAsync("ALTER TABLE published_entries DISABLE TRIGGER ALL");
        await publications.CommitAsync("auth0|owner", EntriesCommit(entries), "key", new string('4', 64), default);
        await ExecuteAsync("UPDATE published_entries SET congress = NULL");
        var backfill = new BackfillStoredColumns(publications, new FakeReleaseDirectory());

        var firstRun = await backfill.ExecuteAsync(PublicationScenario.Owner);
        var secondRun = await backfill.ExecuteAsync(PublicationScenario.Owner);

        Assert.Equal(29, firstRun.EntryCongressesFilled);
        Assert.Equal(0, secondRun.EntryCongressesFilled);
        Assert.Equal(entries.OrderBy(entry => entry.RecordId, StringComparer.Ordinal).Select(entry => ReleaseRecordOrder.CongressOf(entry)?.ToString() ?? ""),
            await ReadAsync("SELECT coalesce(congress::text, '') FROM published_entries ORDER BY record_id COLLATE \"C\""));
    }

    [Fact]
    public async Task TheTallyVerifierRebuildsTheGroupsFromTheStoredMemberListAndReportsAForgedMissingAndExtraRowWithoutWriting()
    {
        var published = await PublishVoteAsync([Member("M000001", Term(0, "2025-01-03", "2027-01-03", "Democrat"))]);
        await SeedAsync(published, published.Vote.RevisionNumber, inRelease: true);
        // The seed holds the tracked members only. A real list also holds each untracked member, whose party is the pinned party of the source letter.
        await ExecuteAsync($"""
            SET session_replication_role = replica;
            INSERT INTO vote_member_list_entries (list_id, "position", person_id, bioguide_id, vote_cast, party, citation_extraction_id, citation_start, citation_length)
            VALUES ('{new string('c', 64)}', 1, '{Second}', '{Second}', 'No', 'Republican', 'ext', 0, 1);
            """);
        await new BackfillStoredColumns(publications, published.Scenario.Releases).ExecuteAsync(PublicationScenario.Owner);
        var verify = new VerifyVotePartyTallies(publications);
        const string snapshotSql = "SELECT coalesce(string_agg(t.party || t.yea || t.nay || t.present || t.not_voting || t.other, ',' ORDER BY t.party), '') || '/' || (SELECT coalesce(max(party_tally_count), -1) FROM vote_revisions) FROM vote_revision_party_tallies t";

        var exact = await verify.ExecuteAsync(PublicationScenario.Owner, 10, null);
        await ExecuteAsync("UPDATE vote_revision_party_tallies SET yea = yea + 1 WHERE party = 'Democrat'");
        var before = await ReadAsync(snapshotSql);
        var forged = await verify.ExecuteAsync(PublicationScenario.Owner, 10, null);
        var after = await ReadAsync(snapshotSql);
        await ExecuteAsync("UPDATE vote_revision_party_tallies SET yea = yea - 1 WHERE party = 'Democrat'; DELETE FROM vote_revision_party_tallies WHERE party = 'Republican'");
        var missing = await verify.ExecuteAsync(PublicationScenario.Owner, 10, null);
        await ExecuteAsync("INSERT INTO vote_revision_party_tallies VALUES (" + $"'{published.Vote.RecordId}', 1, 'Republican', 0, 1, 0, 0, 0), ('{published.Vote.RecordId}', 1, 'Forged', 1, 0, 0, 0, 0)");
        var extra = await verify.ExecuteAsync(PublicationScenario.Owner, 10, null);

        Assert.Equal((1, 1, 0, 0), (exact.Examined, exact.Verified, exact.Failed, exact.Failures.Count));
        Assert.Equal([VotePartyTallyFailure.DifferentCounts], forged.Failures.Select(failure => failure.Reason));
        Assert.Equal(before, after);
        Assert.Equal([VotePartyTallyFailure.MarkerWithoutRows, VotePartyTallyFailure.MissingGroup], missing.Failures.Select(failure => failure.Reason).Order(StringComparer.Ordinal));
        Assert.Equal([VotePartyTallyFailure.ExtraGroup, VotePartyTallyFailure.MarkerWithoutRows], extra.Failures.Select(failure => failure.Reason).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task TheTallyVerifierKeepsAnUnknownCountNullAndNeverWritesZeroWhenNoKeptFileProvesIt()
    {
        var published = await PublishVoteAsync([Member("M000001", Term(0, "2025-01-03", "2027-01-03", "Democrat"))], otherLines: [], totals: [PublicationScenario.DefaultPartyTotal]);
        await SeedAsync(published, published.Vote.RevisionNumber, inRelease: true);
        published.Scenario.Releases.Directories.Clear();
        await new BackfillStoredColumns(publications, published.Scenario.Releases).ExecuteAsync(PublicationScenario.Owner);

        var result = await new VerifyVotePartyTallies(publications).ExecuteAsync(PublicationScenario.Owner, 10, null);

        Assert.Equal((1, 1, 0, 0), (result.Examined, result.Unresolved, result.Verified + result.Unavailable + result.Failed, result.Failures.Count));
        Assert.Equal([""], await ReadAsync("SELECT coalesce(party_tally_count::text, '') FROM vote_revisions"));
        Assert.Equal(0, await ScalarIntAsync("SELECT count(*) FROM vote_revision_party_tallies"));
    }

    [Fact]
    public async Task TheTallyVerifierPagesByRevisionAndReadsEachRevisionOnceBecauseTheCursorIsAKeysetOnRecordAndRevision()
    {
        var published = await PublishVoteAsync([]);
        await SeedAsync(published, 1, throughRevision: 3);
        await ExecuteAsync("UPDATE vote_revisions SET party_tally_count = 0");
        var verify = new VerifyVotePartyTallies(publications);

        var first = await verify.ExecuteAsync(PublicationScenario.Owner, 1, null);
        var second = await verify.ExecuteAsync(PublicationScenario.Owner, 1, VotePartyTallyVerificationCursor.Parse(first.NextCursor));
        var third = await verify.ExecuteAsync(PublicationScenario.Owner, 5, VotePartyTallyVerificationCursor.Parse(second.NextCursor));

        Assert.Equal($"1:{published.Vote.RecordId}", first.NextCursor);
        Assert.Equal($"2:{published.Vote.RecordId}", second.NextCursor);
        Assert.Equal((1, 1, 1, 3), (first.Examined, second.Examined, third.Examined, first.Unavailable + second.Unavailable + third.Unavailable));
        Assert.Null(third.NextCursor);
    }

    private sealed record PublishedVoteRun(PublicationScenario Scenario, PublicationReleaseSummary Release, PublishedVote Vote, PublicationCommit Commit);

    private static async Task<PublishedVoteRun> PublishVoteAsync(RosterMember[] roster, string[]? otherLines = null, string[]? totals = null, string trackedSourceId = "M000001")
    {
        var scenario = new PublicationScenario { RosterMembers = roster };
        var recordId = scenario.AddVote('a', totalLines: totals ?? [PublicationScenario.DefaultPartyTotal, RepublicanNay], otherMemberLines: otherLines ?? [SecondLine], trackedSourceId: trackedSourceId);
        var release = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request("k", new PublishedRecordReference(PublishedRecordKind.Vote, recordId)));
        var vote = JsonSerializer.Deserialize<PublishedVote>(scenario.Releases.Directories[release.DirectoryName][PublicationProtocol.RecordDataPath(recordId)],
            PublicationProtocol.JsonOptions)!;
        return new(scenario, release, vote, scenario.Publications.LastCommit!);
    }

    private async Task WriteThroughCommitAsync(PublishedVoteRun published)
    {
        await SeedAsync(published, published.Vote.RevisionNumber);
        var commit = new PublicationCommit(1, null, $"000001-{Guid.NewGuid():N}", [], []) { Withdrawals = [], Names = ReleaseNameFixtures.None, VoteColumns = published.Commit.VoteColumns };
        await publications.CommitAsync("auth0|owner", commit, "key", new string('5', 64), default);
    }

    // The release file holds what the rule gave: the party of each member, the party majorities, and the manifest alignment.
    private static string[] ExpectedParties(PublishedVote vote) => [.. vote.Members.Select(member => member.Party)];

    private static string ExpectedRevisionColumns(PublishedVote vote) =>
        $"{vote.ToEntry().Alignment}|{vote.MajorityOf(PublishedPartyMajority.Democrat)}|{vote.MajorityOf(PublishedPartyMajority.Republican)}";

    private async Task<string[]> StoredPartiesAsync(PublishedVoteRun published) => await ReadAsync(
        $"SELECT coalesce(party, '') FROM vote_member_list_entries e JOIN vote_revisions r ON r.member_list_id = e.list_id WHERE r.revision_number = {published.Vote.RevisionNumber} ORDER BY e.position");

    private async Task<string> StoredRevisionColumnsAsync(PublishedVoteRun published) => (await ReadAsync(
        $"SELECT coalesce(alignment, '') || '|' || coalesce(democrat_majority, '') || '|' || coalesce(republican_majority, '') FROM vote_revisions WHERE revision_number = {published.Vote.RevisionNumber}"))[0];

    // The rows skip the foreign keys, as the view tests do, because only the columns under test matter here.
    private async Task SeedAsync(PublishedVoteRun published, int revision, bool inRelease = false, int? throughRevision = null)
    {
        var list = new string('c', 64);
        var vote = published.Vote;
        var entries = string.Join('\n', vote.Members.Select((member, position) => $"""
            INSERT INTO vote_member_list_entries (list_id, "position", person_id, bioguide_id, vote_cast, citation_extraction_id, citation_start, citation_length)
            VALUES ('{list}', {position}, '{member.Official.Id}', 'B{position:000000}', '{member.Cast}', 'ext', 0, 1);
            """));
        await ExecuteAsync($"""
            SET session_replication_role = replica;
            INSERT INTO vote_member_lists (list_id, {VoteMemberListSeed.Columns}, created_at_utc_ticks) VALUES ('{list}', {VoteMemberListSeed.Values(vote.Members.Select(member => member.Cast))}, 1);
            {entries}
            INSERT INTO vote_revisions (record_id, revision_number, author_subject, author_kind, created_at_utc_ticks, headline, summary, text_origin, source_id, vote_source,
                chamber, congress, session_number, roll_call_number, candidate_id, candidate_update_utc_ticks, voted_on, voted_on_citation_extraction_id, voted_on_citation_start,
                voted_on_citation_length, kind, member_list_id, issue_revision_id, subjects_hash, issue_candidates_recorded, result)
            VALUES {string.Join(", ", Enumerable.Range(revision, (throughRevision ?? revision) - revision + 1).Select(number => $"""
                ('{vote.RecordId}', {number}, 'test', 'Person', 1, 'Headline', 'Summary', 'Person', 'src', 'CongressGov', 'House', {vote.Congress}, 1, 100, 'cand', 1,
                '{vote.VotedOn:yyyy-MM-dd}', 'ext', 0, 1, 'Passage', '{list}', '{new string('a', 64)}', '{new string('b', 64)}', true, 'Passed')
                """))};
            DO $fixture$
            DECLARE legacy_trigger record;
            BEGIN
                FOR legacy_trigger IN
                    SELECT t.tgname FROM pg_trigger t JOIN pg_constraint c ON c.oid = t.tgconstraint
                    WHERE t.tgrelid = 'vote_revisions'::regclass AND c.conrelid = 'vote_revisions'::regclass AND c.contype = 'f'
                LOOP
                    EXECUTE format('ALTER TABLE vote_revisions DISABLE TRIGGER %I', legacy_trigger.tgname);
                END LOOP;
            END $fixture$;
            """);
        if (!inRelease) return;
        var directory = $"000001-{Guid.NewGuid():N}";
        published.Scenario.Releases.Directories[directory] = published.Scenario.Releases.Directories[published.Release.DirectoryName];
        await ExecuteAsync($"""
            SET session_replication_role = replica;
            INSERT INTO publication_releases (release_number, directory_name, actor_subject, published_at_utc_ticks, record_count, is_active)
            VALUES (1, '{directory}', 'test', 1, 1, true);
            INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release) VALUES ('vote', '{vote.RecordId}', 1, {revision}, NULL);
            """);
    }

    private static PublicationCommit NamesCommit(PublishedOfficialProfile[] officials) => new(1, null, $"000001-{Guid.NewGuid():N}", [], [])
    {
        Withdrawals = [],
        Names = new([.. officials], new([], []))
    };

    private static PublicationCommit EntriesCommit(PublishedRecordEntry[] entries) => new(1, null, $"000001-{Guid.NewGuid():N}", [], [])
    {
        Withdrawals = [],
        AddedEntries = [.. entries],
        Names = ReleaseNameFixtures.None
    };

    private static PublishedOfficialProfile Official(string id, PublishedPhoto? photo) => new()
    {
        Id = id,
        Name = id,
        Photo = photo,
        Terms = [new() { Office = "Senator", Kind = PublishedOfficeKind.Senator, State = "WA", Party = "Democratic", Start = new(2020, 1, 3) }]
    };

    private static RosterMember Member(string id, params RosterTerm[] terms) => new(id, id, null, null, null, terms);

    private static RosterTerm Term(int index, string start, string end, string party) =>
        new(index, "rep", DateOnly.Parse(start), DateOnly.Parse(end), "MA", 1, null, party, null);

    // A vote file of a revision. The revision number and the member parties differ. A later revision swaps the Yea and Nay counts of the parties, so its columns differ too.
    private static PublishedVote FileOf(PublishedVoteRun published, int revision, string party)
    {
        var vote = published.Vote with
        {
            RevisionNumber = revision,
            Members = [.. published.Vote.Members.Select(member => member with { Party = party })],
            PartyMajorities = [.. published.Vote.PartyMajorities.Select(majority => revision == 1 ? majority : PublishedPartyMajority.Of(majority.Party, majority.NayCount, majority.YeaCount))],
            PartyTallies = published.Vote.PartyTallies?.Select(tally => revision == 1 ? tally : tally with { Yea = tally.Nay, Nay = tally.Yea }).ToArray()
        };
        vote.Validate();
        return vote;
    }

    // Revision n sits in release n. A null file leaves the folder of that release without the record file. A null last release leaves the range open.
    private async Task SeedReleasesAsync(PublishedVoteRun published, params (int Revision, PublishedVote? File, int? LastRelease)[] revisions)
    {
        foreach (var (revision, file, last) in revisions)
        {
            var directory = $"{revision:D6}-{Guid.NewGuid():N}";
            published.Scenario.Releases.Directories[directory] = file is null ? [] : new()
            {
                [PublicationProtocol.RecordDataPath(file.RecordId)] = JsonSerializer.SerializeToUtf8Bytes(file, PublicationProtocol.JsonOptions)
            };
            await ExecuteAsync($"""
                SET session_replication_role = replica;
                INSERT INTO publication_releases (release_number, directory_name, actor_subject, published_at_utc_ticks, record_count, is_active)
                VALUES ({revision}, '{directory}', 'test', 1, 1, {(last is null ? "true" : "false")});
                INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release)
                VALUES ('vote', '{published.Vote.RecordId}', {revision}, {revision}, {(last is null ? "NULL" : last.ToString())});
                """);
        }
    }

    private async Task<int> ExitOfBackfillAsync(FakeReleaseDirectory releases) => await ReleaseCommand.ExecuteAsync(["releases", "columns", "backfill"], null!, null!, null!, null!, null!,
        null!, new BackfillStoredColumns(publications, releases), null!, null!, null!, null!, null!, null!, null!, null!, PublicationScenario.Owner, TextWriter.Null, TextWriter.Null, CancellationToken.None);

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<int> ScalarIntAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task<string[]> ReadAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync()) rows.Add(Convert.ToString(reader.GetValue(0))!);
        return [.. rows];
    }
}
