using System.Text.Json;
using CivicLens.Application.Review;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.ExecutiveActions;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.ExecutiveActions;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresExecutiveActionDispositionTargetsTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string Notes = "Amends: EO 13224, September 23, 2001\r\nRevokes: EO 14056, December 1, 2021; Proc. 9822; SPD 3\r\nSee: EO 14193, February 1, 2025";
    private const string TargetSql = "SELECT relation || '/' || kind || '/' || number || '/' || reference_start || '/' || reference_length FROM executive_action_revision_disposition_targets";
    private static readonly ReviewActor Owner = new("auth0|owner", ReviewRole.Owner);

    private readonly string captureRoot = Path.Combine(Path.GetTempPath(), "executive-action-targets-" + Guid.NewGuid().ToString("N"));
    private string connectionString = null!;
    private ExecutiveActionSeeder executiveActions = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        executiveActions = new ExecutiveActionSeeder(connectionString, captureRoot);
    }

    public async Task DisposeAsync()
    {
        await postgres.DropDatabaseAsync(connectionString);
        Directory.Delete(captureRoot, recursive: true);
    }

    [Fact]
    public async Task AnAdmittedRevisionStoresExactTargetsWithTheirSpansAndNoteLessRevisionsStoreNone()
    {
        await executiveActions.AdmitAsync(WithNotes(FederalRegisterFixtures.TrumpOrder, Notes), "donald-trump");
        await executiveActions.AdmitAsync(FederalRegisterFixtures.BidenProclamation, "joe-biden");

        var rows = await TargetsAsync();

        Assert.Equal(["Amends/ExecutiveOrder/13224/8/28", "Revokes/ExecutiveOrder/14056/47/26", "Revokes/Proclamation/9822/75/10"], rows);
        Assert.Equal(["EO 13224, September 23, 2001", "EO 14056, December 1, 2021", "Proc. 9822"],
            rows.Select(row => row.Split('/')).Select(part => Notes.Substring(int.Parse(part[3]), int.Parse(part[4]))));
        Assert.Equal(1L, await ScalarAsync("SELECT count(DISTINCT record_id) FROM executive_action_revision_disposition_targets"));
        Assert.Equal(2L, await ScalarAsync("SELECT count(*) FROM executive_action_revisions"));
    }

    [Fact]
    public async Task ACorrectionRevisionKeepsTheOriginalTargetsWithTheOriginalNote()
    {
        await executiveActions.AdmitAsync(WithNotes(FederalRegisterFixtures.BidenProclamation, Notes), "joe-biden");
        await executiveActions.AdmitAsync(WithNotes(FederalRegisterFixtures.BidenProclamationCorrection, "Revokes: EO 99999"), "joe-biden");

        Assert.Equal(2L, await ScalarAsync("SELECT count(*) FROM executive_action_revisions"));
        Assert.Equal(3L, await ScalarAsync("SELECT count(*) FROM executive_action_revision_disposition_targets WHERE revision_number = 1"));
        Assert.Equal(await ScalarAsync(RevisionTargets(1)), await ScalarAsync(RevisionTargets(2)));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM executive_action_revision_disposition_targets WHERE number = '99999'"));
    }

    [Fact]
    public async Task TheBackfillWritesTargetsForOldRevisionsInBatchesAndARerunChangesNothing()
    {
        await executiveActions.AdmitAsync(WithNotes(FederalRegisterFixtures.TrumpOrder, Notes), "donald-trump");
        await executiveActions.AdmitAsync(WithNotes(FederalRegisterFixtures.BidenProclamation, "Revokes: EO 14056"), "joe-biden");
        var expected = await TargetsAsync();
        await ExecuteAsync("DELETE FROM executive_action_revision_disposition_targets");
        Assert.Equal((2, 2), await VerifyAsync(legacy: true));

        var first = await BackfillAsync(10);
        var snapshot = await ScalarAsync("SELECT coalesce(string_agg(to_jsonb(t)::text, '|' ORDER BY to_jsonb(t)::text), '') FROM executive_action_revision_disposition_targets t");
        var rerun = await BackfillAsync(10);

        Assert.Equal((2, 0), (first.Moved, first.Skipped));
        Assert.Equal(expected.Order(), (await TargetsAsync()).Order());
        Assert.Equal(0, rerun.Moved);
        Assert.Equal(snapshot, await ScalarAsync("SELECT coalesce(string_agg(to_jsonb(t)::text, '|' ORDER BY to_jsonb(t)::text), '') FROM executive_action_revision_disposition_targets t"));
        Assert.Equal((2, 0), await VerifyAsync(legacy: false));
    }

    [Fact]
    public async Task TheBackfillStopsAtTheLimitAndNotesWithNoTargetStayNotesOnly()
    {
        await executiveActions.AdmitAsync(WithNotes(FederalRegisterFixtures.TrumpOrder, "See: EO 14186"), "donald-trump");
        await executiveActions.AdmitAsync(WithNotes(FederalRegisterFixtures.BidenProclamation, Notes), "joe-biden");
        await ExecuteAsync("DELETE FROM executive_action_revision_disposition_targets");

        var result = await BackfillAsync(1);

        Assert.Equal(1, result.Moved);
        Assert.Equal(3L, await ScalarAsync("SELECT count(*) FROM executive_action_revision_disposition_targets"));
        Assert.Equal((2, 0), await VerifyAsync(legacy: false));
    }

    [Fact]
    public async Task VerifyReportsAMissingTargetAsLegacyAndAChangedOrOrphanTargetAsAMismatchByIdOnly()
    {
        var recordId = await executiveActions.AdmitAsync(WithNotes(FederalRegisterFixtures.TrumpOrder, Notes), "donald-trump");
        await ExecuteAsync("DELETE FROM executive_action_revision_disposition_targets WHERE position = 2");
        var missingOne = await VerifyAsync();
        await ExecuteAsync("UPDATE executive_action_revision_disposition_targets SET number = '14057' WHERE position = 1");
        var mismatch = await VerifyAsync();
        await ExecuteAsync("DELETE FROM executive_action_revision_disposition_targets");
        var legacy = await VerifyAsync();
        await ExecuteAsync("UPDATE executive_action_revisions SET disposition_notes = NULL, disposition_notes_extraction_id = NULL, disposition_notes_start = NULL, disposition_notes_length = NULL");
        await ExecuteAsync("INSERT INTO executive_action_revision_disposition_targets VALUES (@record, 1, 0, 'Amends', 'ExecutiveOrder', '1', 0, 1)", ("record", recordId));
        var orphan = await VerifyAsync();

        Assert.Equal((1, 0, 1), (missingOne.Checked, missingOne.Legacy, missingOne.Mismatched));
        Assert.Equal(["dispositionTargets"], Assert.Single(mismatch.FirstMismatches).Fields);
        Assert.Equal((1, 1, 0), (legacy.Checked, legacy.Legacy, legacy.Mismatched));
        Assert.Equal((1, 0, 1), (orphan.Checked, orphan.Legacy, orphan.Mismatched));
        Assert.Equal(recordId, Assert.Single(orphan.FirstMismatches).RecordId);
    }

    [Theory]
    [InlineData("INSERT INTO executive_action_revision_disposition_targets (record_id, revision_number, position, relation, kind, number, reference_start, reference_length) SELECT record_id, revision_number, 9, 'See', 'ExecutiveOrder', '1', 0, 1 FROM executive_action_revisions", "ck_executive_action_revision_disposition_targets_relation")]
    [InlineData("INSERT INTO executive_action_revision_disposition_targets (record_id, revision_number, position, relation, kind, number, reference_start, reference_length) SELECT record_id, revision_number, 9, 'Amends', 'Notice', '1', 0, 1 FROM executive_action_revisions", "ck_executive_action_revision_disposition_targets_kind")]
    [InlineData("INSERT INTO executive_action_revision_disposition_targets (record_id, revision_number, position, relation, kind, number, reference_start, reference_length) SELECT record_id, revision_number, 9, 'Amends', 'ExecutiveOrder', '01', 0, 1 FROM executive_action_revisions", "ck_executive_action_revision_disposition_targets_number")]
    [InlineData("INSERT INTO executive_action_revision_disposition_targets (record_id, revision_number, position, relation, kind, number, reference_start, reference_length) SELECT record_id, revision_number, 9, 'Amends', 'ExecutiveOrder', '1', 0, 0 FROM executive_action_revisions", "ck_executive_action_revision_disposition_targets_span")]
    [InlineData("INSERT INTO executive_action_revision_disposition_targets (record_id, revision_number, position, relation, kind, number, reference_start, reference_length) SELECT record_id, revision_number, 9, 'Amends', 'ExecutiveOrder', '13224', 0, 1 FROM executive_action_revisions", "IX_executive_action_revision_disposition_targets_record_id_rev~")]
    public async Task TheDatabaseRefusesATargetThatBreaksARule(string change, string constraint)
    {
        await executiveActions.AdmitAsync(WithNotes(FederalRegisterFixtures.TrumpOrder, Notes), "donald-trump");

        var thrown = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(change));

        Assert.Equal(constraint, thrown.ConstraintName);
    }

    [Fact]
    public async Task ARevisionWithTargetsCannotBeDeleted()
    {
        await executiveActions.AdmitAsync(WithNotes(FederalRegisterFixtures.TrumpOrder, Notes), "donald-trump");

        var thrown = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync("DELETE FROM executive_action_revisions"));

        Assert.Equal(PostgresErrorCodes.RestrictViolation, thrown.SqlState);
    }

    private static string RevisionTargets(int revision) =>
        $"SELECT string_agg(position || relation || kind || number || reference_start || reference_length, '|' ORDER BY position) FROM executive_action_revision_disposition_targets WHERE revision_number = {revision}";

    private static string WithNotes(string json, string notes) =>
        json.Replace("\"html_url\":", $"\"disposition_notes\":{JsonSerializer.Serialize(notes)},\"html_url\":", StringComparison.Ordinal);

    private Task<RevisionBackfillResult> BackfillAsync(int limit) => new BackfillRevisions(Store()).ExecuteAsync(Owner, limit);

    private Task<RevisionVerification> VerifyAsync() => new VerifyRevisions(Store()).ExecuteAsync(Owner);

    private async Task<(int Checked, int Mismatched)> VerifyAsync(bool legacy)
    {
        var result = await VerifyAsync();
        Assert.Equal(legacy, result.Legacy > 0);
        Assert.Equal(0, result.Mismatched);
        return (result.Checked, result.Legacy);
    }

    private PostgresExecutiveActionTargetBackfillStore Store() => new(new PooledDbContextFactory<CollectionAttemptDbContext>(
        new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options));

    private async Task<string[]> TargetsAsync()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"{TargetSql} ORDER BY revision_number, position", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync()) rows.Add(reader.GetString(0));
        return [.. rows];
    }

    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<object?> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync();
        return value is DBNull ? null : value;
    }
}
