using System.Collections.Immutable;
using System.Text.Json;
using CivicLens.Application.Publication;
using CivicLens.Application.Roster;
using CivicLens.Core.Votes;
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

    [Theory]
    [InlineData("Democrat", "2025-03-31", "Republican", "Republican")]
    [InlineData("Republican", "2025-06-30", "Democrat", "Republican")]
    public async Task AMemberWhoChangedPartyMidCongressStoresThePartyOfTheTermThatCoversTheVoteDayBecauseTheReleaseUsesThatTerm(string before, string lastDay,
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
    public async Task AMemberListThatAnotherRevisionFilledKeepsItsPartiesAndRefusesDifferentOnesBecauseAListHasOneParty()
    {
        RosterMember[] roster = [Member("M000001", Term(0, "2025-01-03", "2027-01-03", "Democrat")), Member(Second, Term(0, "2025-01-03", "2027-01-03", "Republican"))];
        var published = await PublishVoteAsync(roster);
        await SeedAsync(published, published.Vote.RevisionNumber, extraRevision: 2);
        var columns = VoteStoredColumns.From(published.Vote);
        Assert.Empty(await publications.WriteVoteColumnsAsync([columns], default));

        var refused = await publications.WriteVoteColumnsAsync([columns with { RevisionNumber = 2, MemberParties = ["Republican"] }], default);

        Assert.Equal([2], refused.Select(binding => binding.RevisionNumber));
        Assert.Equal(["Democrat"], await StoredPartiesAsync(published));
        Assert.Equal(["||"], await ReadAsync("SELECT coalesce(alignment, '') || '|' || coalesce(democrat_majority, '') || '|' || coalesce(republican_majority, '') FROM vote_revisions WHERE revision_number = 2"));
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
            PublishedEntryFixtures.Action(new string('c', 64), first) with { EventDate = null }
        ];

        await ExecuteAsync("ALTER TABLE published_entries DISABLE TRIGGER ALL");
        await publications.CommitAsync("auth0|owner", EntriesCommit(entries), "key", new string('2', 64), default);

        Assert.Equal(["a|118", "b|119", "c|119"], await ReadAsync("SELECT left(record_id, 1) || '|' || congress FROM published_entries ORDER BY record_id"));
        Assert.Equal(entries.Select(entry => entry.RecordId[..1] + "|" + ReleaseRecordOrder.CongressOf(entry)), await ReadAsync("SELECT left(record_id, 1) || '|' || congress FROM published_entries ORDER BY record_id"));
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
        Assert.Equal(stored, await StoredPartiesAsync(published));
        Assert.Equal(columns, await StoredRevisionColumnsAsync(published));
        Assert.Equal(0, second.VoteRevisionsWithOtherParties);
        Assert.Equal(0, second.VoteRevisionsWithoutKeptFile);
    }

    [Fact]
    public async Task TheBackfillReportsAVoteRevisionThatNoKeptFolderHoldsAndLeavesItUnwritten()
    {
        var published = await PublishVoteAsync([Member("M000001", Term(0, "2025-01-03", "2027-01-03", "Democrat"))], otherLines: [], totals: [PublicationScenario.DefaultPartyTotal]);
        await SeedAsync(published, published.Vote.RevisionNumber, inRelease: true);
        published.Scenario.Releases.Directories.Clear();

        var result = await new BackfillStoredColumns(publications, published.Scenario.Releases).ExecuteAsync(PublicationScenario.Owner);

        Assert.Equal(1, result.VoteRevisionsWithoutKeptFile);
        Assert.Equal(0, result.VoteRevisionsWritten);
        Assert.Equal([""], await StoredPartiesAsync(published));
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
    private async Task SeedAsync(PublishedVoteRun published, int revision, bool inRelease = false, int? extraRevision = null)
    {
        var list = new string('c', 64);
        var vote = published.Vote;
        var entries = string.Join('\n', vote.Members.Select((member, position) => $"""
            INSERT INTO vote_member_list_entries (list_id, "position", person_id, bioguide_id, vote_cast, citation_extraction_id, citation_start, citation_length)
            VALUES ('{list}', {position}, '{member.Official.Id}', 'B{position:000000}', '{member.Cast}', 'ext', 0, 1);
            """));
        await ExecuteAsync($"""
            SET session_replication_role = replica;
            INSERT INTO vote_member_lists (list_id, member_count, created_at_utc_ticks) VALUES ('{list}', {vote.Members.Length}, 1);
            {entries}
            INSERT INTO vote_revisions (record_id, revision_number, author_subject, author_kind, created_at_utc_ticks, headline, summary, text_origin, source_id, vote_source,
                chamber, congress, session_number, roll_call_number, candidate_id, candidate_update_utc_ticks, voted_on, voted_on_citation_extraction_id, voted_on_citation_start,
                voted_on_citation_length, kind, member_list_id, issue_revision_id, subjects_hash, issue_candidates_recorded, result)
            VALUES {string.Join(", ", new[] { revision }.Concat(extraRevision is { } extra ? [extra] : []).Select(number => $"""
                ('{vote.RecordId}', {number}, 'test', 'Person', 1, 'Headline', 'Summary', 'Person', 'src', 'CongressGov', 'House', {vote.Congress}, 1, 100, 'cand', 1,
                '{vote.VotedOn:yyyy-MM-dd}', 'ext', 0, 1, 'Passage', '{list}', '{new string('a', 64)}', '{new string('b', 64)}', true, 'Passed')
                """))};
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

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
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
