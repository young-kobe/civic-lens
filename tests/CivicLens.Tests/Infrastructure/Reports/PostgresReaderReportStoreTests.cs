using CivicLens.Application.Paging;
using CivicLens.Application.Publication;
using CivicLens.Application.Reports;
using CivicLens.Application.Review;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Reports;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Npgsql;
using static CivicLens.Tests.Publication.Contracts.PulledReportTests;

namespace CivicLens.Tests.Infrastructure.Reports;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresReaderReportStoreTests(PostgresCollection postgres) : IAsyncLifetime
{
    private static readonly string RelationshipId = new('a', 64);
    private static readonly DateTimeOffset Start = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    private string connectionString = null!;
    private PostgresReaderReportStore store = null!;
    private BriefWorldSeeder releases = null!;
    private RelationshipWorldSeeder seeder = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        store = new PostgresReaderReportStore(AnalysisDbContextFactory.Create(connectionString));
        releases = new BriefWorldSeeder(connectionString);
        seeder = new RelationshipWorldSeeder(connectionString);
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task APageOfReportsIsStoredOpenWithItsCursorAndNothingElseAboutTheReporter()
    {
        var report = Report() with { Text = "The date is wrong." };

        await store.SavePulledAsync(Batch(report), default);

        Assert.Equal("epoch.1", await store.GetCursorAsync(default));
        var item = (await store.GetAsync(report.ReportId, default))!;
        Assert.Equal((ReaderReportStatus.Open, 0, report.Text, report.RevisionNumber), (item.Report.Status, item.Report.Version, item.Report.Text, item.Report.RevisionNumber));
        Assert.Equal(["report_id", "record_id", "kind", "revision_number", "category", "body", "received_at_utc_ticks", "status", "note",
            "resolved_by", "resolved_at_utc_ticks", "version"], (await seeder.ReadAsync(
            "SELECT column_name FROM information_schema.columns WHERE table_name = 'reader_reports' ORDER BY ordinal_position")).Select(row => row[0]));
    }

    [Fact]
    public async Task ACursorThatCannotBeSavedLeavesNoReportsBehindSoAReplayNeverSkipsAndNeverHalfStores()
    {
        await store.SavePulledAsync(Batch(Report()), default);
        var tooLong = Batch(Report(), Report()) with { NextCursor = new string('x', 101) };

        await Assert.ThrowsAnyAsync<Exception>(() => store.SavePulledAsync(tooLong, default));

        Assert.Equal("epoch.1", await store.GetCursorAsync(default));
        Assert.Equal(1, await store.CountOpenAsync(default));
    }

    [Fact]
    public async Task AReplayAfterAnEpochChangeStoresEachReportOnceAndKeepsTheResolutionOfOneThatIsAlreadyHandled()
    {
        var first = Report();
        var second = Report();
        await store.SavePulledAsync(Batch(first, second), default);
        await store.ResolveAsync("auth0|friend", first.ReportId, 0, ReaderReportStatus.Dismissed, null, default);

        await store.SavePulledAsync(Batch(first, second, Report()) with { NextCursor = "other-epoch.3" }, default);

        Assert.Equal("other-epoch.3", await store.GetCursorAsync(default));
        Assert.Equal(2, await store.CountOpenAsync(default));
        Assert.Equal(ReaderReportStatus.Dismissed, (await store.GetAsync(first.ReportId, default))!.Report.Status);
        Assert.Equal(3L, long.Parse((await seeder.ReadAsync("SELECT count(*) FROM reader_reports"))[0][0]));
    }

    [Fact]
    public async Task TheOpenQueueListsTheOldestReportFirstThenTheReportIdAscendingAndPagesBothWaysWithoutSkippingATie()
    {
        var reports = new[] { Received(0), Received(2), Received(1), Received(1), Received(3) };
        await store.SavePulledAsync(Batch(reports), default);
        var expected = reports.OrderBy(report => report.ReceivedAtUtc).ThenBy(report => report.ReportId.ToString("D"), StringComparer.Ordinal)
            .Select(report => report.ReportId.ToString("D")).ToArray();

        var walk = await PageWalk.RunAsync(cursor => ReadPageAsync(cursor, ReaderReportFilter.Open));

        walk.AssertExact(expected);
    }

    [Fact]
    public async Task TheResolvedListShowsTheNewestReportFirstThenTheReportIdDescendingAndPagesBothWays()
    {
        var reports = new[] { Received(0), Received(2), Received(1), Received(1), Received(3) };
        await store.SavePulledAsync(Batch(reports), default);
        foreach (var report in reports) await store.ResolveAsync("auth0|friend", report.ReportId, 0, ReaderReportStatus.Dismissed, null, default);
        await store.SavePulledAsync(Batch(Received(9)), default);
        var expected = reports.OrderByDescending(report => report.ReceivedAtUtc).ThenByDescending(report => report.ReportId.ToString("D"), StringComparer.Ordinal)
            .Select(report => report.ReportId.ToString("D")).ToArray();

        var walk = await PageWalk.RunAsync(cursor => ReadPageAsync(cursor, ReaderReportFilter.Resolved));

        walk.AssertExact(expected);
    }

    [Fact]
    public async Task AResolvedReportLeavesTheOpenQueueAndIsKeptInTheResolvedList()
    {
        var report = Report();
        await store.SavePulledAsync(Batch(report, Report()), default);

        await store.ResolveAsync("auth0|friend", report.ReportId, 0, ReaderReportStatus.CorrectionNeeded, "Fix the quote.", default);

        Assert.Equal(1, await store.CountOpenAsync(default));
        var resolved = Assert.Single((await store.ListAsync(null, 10, ReaderReportFilter.Resolved, default)).Items).Report;
        Assert.Equal((report.ReportId, ReaderReportStatus.CorrectionNeeded, "Fix the quote.", "auth0|friend", 1),
            (resolved.ReportId, resolved.Status, resolved.Note, resolved.ResolvedBy, resolved.Version));
        Assert.NotNull(resolved.ResolvedAtUtc);
        Assert.DoesNotContain(report.ReportId, (await store.ListAsync(null, 10, ReaderReportFilter.Open, default)).Items.Select(item => item.Report.ReportId));
    }

    [Fact]
    public async Task AReportThatIsResolvedOrChangedCannotBeResolvedAgainAndAMissingOneIsNotFound()
    {
        var report = Report();
        await store.SavePulledAsync(Batch(report), default);
        await Assert.ThrowsAsync<ReviewConflictException>(() => store.ResolveAsync("auth0|friend", report.ReportId, 5, ReaderReportStatus.Dismissed, null, default));
        await store.ResolveAsync("auth0|friend", report.ReportId, 0, ReaderReportStatus.Dismissed, null, default);

        await Assert.ThrowsAsync<ReviewConflictException>(() => store.ResolveAsync("auth0|owner", report.ReportId, 1, ReaderReportStatus.CorrectionNeeded, "Late.", default));
        await Assert.ThrowsAsync<ReviewConflictException>(() => store.ResolveAsync("auth0|owner", report.ReportId, 0, ReaderReportStatus.CorrectionNeeded, "Late.", default));
        await Assert.ThrowsAsync<ArgumentException>(() => store.ResolveAsync("auth0|owner", Guid.NewGuid(), 0, ReaderReportStatus.Dismissed, null, default));

        var kept = (await store.GetAsync(report.ReportId, default))!.Report;
        Assert.Equal((ReaderReportStatus.Dismissed, "auth0|friend"), (kept.Status, kept.ResolvedBy));
    }

    [Fact]
    public async Task AReportOnAReplacedRevisionShowsTheCurrentRevisionAndOneOnAnUnknownRecordShowsNone()
    {
        await SeedRelationshipDraftAsync(RelationshipId, currentRevision: 3);
        var onOld = Report() with { RecordId = RelationshipId, Kind = PublishedRecordKind.Relationship, RevisionNumber = 1 };
        var unknown = Report() with { RecordId = new string('b', 64), Kind = PublishedRecordKind.Relationship };
        var wrongKind = Report() with { RecordId = RelationshipId, Kind = PublishedRecordKind.Action };
        await store.SavePulledAsync(Batch(onOld, unknown, wrongKind), default);

        var items = (await store.ListAsync(null, 10, ReaderReportFilter.Open, default)).Items.ToDictionary(item => item.Report.ReportId);

        Assert.Equal(3, items[onOld.ReportId].CurrentRevisionNumber);
        Assert.Null(items[unknown.ReportId].CurrentRevisionNumber);
        Assert.Null(items[wrongKind.ReportId].CurrentRevisionNumber);
        Assert.Null((await store.GetAsync(unknown.ReportId, default))!.CurrentRevisionNumber);
    }

    [Theory]
    [MemberData(nameof(EveryRecordKind))]
    public async Task EveryRecordKindHasACurrentRevisionPathSoNoNewKindShowsNoEditorLinkOnItsReports(PublishedRecordKind kind)
    {
        var recordId = new string('a', kind == PublishedRecordKind.DocumentChange ? 32 : 64);
        await seeder.ExecuteAsync(DraftInsert(kind), ("id", recordId));
        var report = Report() with { RecordId = recordId, Kind = kind, RevisionNumber = 1 };
        await store.SavePulledAsync(Batch(report), default);

        var item = (await store.GetAsync(report.ReportId, default))!;

        Assert.Equal(2, item.CurrentRevisionNumber);
    }

    public static IEnumerable<object[]> EveryRecordKind() => Enum.GetValues<PublishedRecordKind>().Select(kind => new object[] { kind });

    private static string DraftInsert(PublishedRecordKind kind) => "SET session_replication_role = replica;\n" + kind switch
    {
        PublishedRecordKind.DocumentChange => """
            INSERT INTO document_change_drafts (draft_id, comparison_id, current_revision_number, review_state_version, review_status, created_at_utc_ticks)
            VALUES (@id, repeat('c', 64), 2, 0, 'AwaitingReview', 0)
            """,
        PublishedRecordKind.Statement => """
            INSERT INTO statement_drafts (candidate_id, current_revision_number, review_state_version, review_status, created_at_utc_ticks)
            VALUES (@id, 2, 0, 'AwaitingReview', 0)
            """,
        PublishedRecordKind.Action => """
            INSERT INTO action_drafts (record_id, source_id, congress, bill_type, bill_number, current_revision_number, created_at_utc_ticks, last_revised_at_utc_ticks, review_state_version, review_status)
            VALUES (@id, 'congress-bills', 119, 'hr', 7, 2, 0, 0, 0, 'AwaitingReview')
            """,
        PublishedRecordKind.Vote => """
            INSERT INTO vote_drafts (record_id, source_id, chamber, congress, session_number, roll_call_number, current_revision_number, review_state_version, review_status, created_at_utc_ticks, last_revised_at_utc_ticks)
            VALUES (@id, 'congress-house-votes', 'House', 119, 1, 100, 2, 0, 'AwaitingReview', 0, 0)
            """,
        PublishedRecordKind.Brief => """
            INSERT INTO brief_drafts (record_id, week_start, current_revision_number, review_state_version, review_status, created_at_utc_ticks, last_revised_at_utc_ticks)
            VALUES (@id, '2026-01-05', 2, 0, 'AwaitingReview', 0, 0)
            """,
        PublishedRecordKind.Relationship => """
            INSERT INTO relationship_drafts (record_id, current_revision_number, review_state_version, review_status, no_longer_matches, created_at_utc_ticks, last_revised_at_utc_ticks)
            VALUES (@id, 2, 0, 'AwaitingReview', false, 0, 0)
            """,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Record kind has no draft insert.")
    };

    [Fact]
    public async Task ACorrectionStaysUnansweredUntilThePublishedRevisionPassesTheRevisionOfTheReport()
    {
        await SeedRelationshipDraftAsync(RelationshipId, currentRevision: 3);
        var report = Report() with { RecordId = RelationshipId, Kind = PublishedRecordKind.Relationship, RevisionNumber = 2 };
        var dismissed = Report() with { RecordId = RelationshipId, Kind = PublishedRecordKind.Relationship, RevisionNumber = 2 };
        var elsewhere = Report() with { RecordId = new string('b', 64), Kind = PublishedRecordKind.Relationship, RevisionNumber = 2 };
        await store.SavePulledAsync(Batch(report, dismissed, elsewhere), default);
        await store.ResolveAsync("auth0|friend", report.ReportId, 0, ReaderReportStatus.CorrectionNeeded, "Fix it.", default);
        await store.ResolveAsync("auth0|friend", dismissed.ReportId, 0, ReaderReportStatus.Dismissed, "Fine.", default);
        await store.ResolveAsync("auth0|friend", elsewhere.ReportId, 0, ReaderReportStatus.CorrectionNeeded, "Fix it.", default);

        Assert.Equal([report.ReportId], await UnansweredAsync());
        await PublishAsync(1, revision: 2);
        Assert.Equal([report.ReportId], await UnansweredAsync());
        await PublishAsync(2, revision: 3);
        Assert.Empty(await UnansweredAsync());
    }

    [Fact]
    public async Task TheDatabaseRefusesACorrectionWithoutANoteAndAnOpenReportWithAResolverBecauseTheQueueRulesDoNotDependOnTheCode()
    {
        var record = new string('a', 64);

        foreach (var (status, note) in new[] { ("CorrectionNeeded", "NULL"), ("Open", "NULL") })
        {
            var failure = await Assert.ThrowsAsync<PostgresException>(() => seeder.ExecuteAsync(InsertReport(record, status, note)));
            Assert.Equal("ck_reader_reports_resolution", failure.ConstraintName);
        }
        await seeder.ExecuteAsync(InsertReport(record, "CorrectionNeeded", "'Fix it.'"));
    }

    [Fact]
    public async Task OnlyClosedReportsResolvedMoreThanAYearAgoAreDeletedSoAnOpenReportAndARecentOneAreNeverLost()
    {
        Assert.Equal(365, ReaderReportProtocol.ClosedRetentionDays);
        var day = TimeSpan.TicksPerDay;
        var record = new string('b', 64);
        var expiredDismissed = await InsertAsync(record, "Dismissed", null, -(ReaderReportProtocol.ClosedRetentionDays * day + day));
        var expiredCorrection = await InsertAsync(record, "CorrectionNeeded", "Fix it.", -(ReaderReportProtocol.ClosedRetentionDays * day + day));
        var insideWindow = await InsertAsync(record, "Dismissed", null, -(ReaderReportProtocol.ClosedRetentionDays * day - day));
        var ancientOpen = await InsertAsync(record, "Open", null, null);

        var deleted = await store.DeleteExpiredClosedAsync(default);

        Assert.Equal(2, deleted);
        Assert.Null(await store.GetAsync(expiredDismissed, default));
        Assert.Null(await store.GetAsync(expiredCorrection, default));
        Assert.NotNull(await store.GetAsync(insideWindow, default));
        Assert.NotNull(await store.GetAsync(ancientOpen, default));
    }

    private async Task<Guid> InsertAsync(string record, string status, string? note, long? resolvedOffsetTicks)
    {
        var id = Guid.NewGuid();
        var resolvedAt = resolvedOffsetTicks is { } offset ? DateTimeOffset.UtcNow.Ticks + offset : (long?)null;
        await seeder.ExecuteAsync("""
            INSERT INTO reader_reports (report_id, record_id, kind, revision_number, category, body, received_at_utc_ticks, status, note,
                resolved_by, resolved_at_utc_ticks, version)
            VALUES (@id, @record, 'Statement', 1, 'other', 'text', 1, @status, @note,
                CASE WHEN @resolvedAt::bigint IS NULL THEN NULL ELSE 'auth0|friend' END, @resolvedAt::bigint, 0)
            """, ("id", id), ("record", record), ("status", status), ("note", (object?)note ?? DBNull.Value), ("resolvedAt", (object?)resolvedAt ?? DBNull.Value));
        return id;
    }

    private static string InsertReport(string record, string status, string note) => $"""
        INSERT INTO reader_reports (report_id, record_id, kind, revision_number, category, body, received_at_utc_ticks, status, note,
            resolved_by, resolved_at_utc_ticks, version)
        VALUES (gen_random_uuid(), '{record}', 'Statement', 1, 'other', 'text', 1, '{status}', {note}, 'auth0|friend', 1, 0)
        """;

    private async Task<(string[] Ids, string? Newer, string? Older)> ReadPageAsync(PageCursor? cursor, ReaderReportFilter filter)
    {
        var page = await store.ListAsync(cursor, 2, filter, default);
        return ([.. page.Items.Select(item => item.Report.ReportId.ToString("D"))], page.NewerCursor, page.OlderCursor);
    }

    private async Task<Guid[]> UnansweredAsync() =>
        [.. (await store.ListUnansweredCorrectionsAsync(PublishedRecordKind.Relationship, RelationshipId, default)).Select(report => report.ReportId)];

    private Task PublishAsync(int number, int revision) =>
        releases.ReplaceActiveReleaseAsync(number, Start, new PublishedRevisionBinding(PublishedRecordKind.Relationship, RelationshipId, revision));

    private Task SeedRelationshipDraftAsync(string recordId, int currentRevision) => seeder.ExecuteAsync("""
        INSERT INTO relationship_drafts (record_id, current_revision_number, review_state_version, review_status, no_longer_matches,
            created_at_utc_ticks, last_revised_at_utc_ticks)
        VALUES (@id, @revision, 0, 'AwaitingReview', false, 1, 1)
        """, ("id", recordId), ("revision", currentRevision));

    private static PulledReport Received(int minutes) => Report() with { ReceivedAtUtc = Start.AddMinutes(minutes) };
}
