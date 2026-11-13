using CivicLens.Application.Publication;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.PublicApi;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class CorrectionHistoryViewsTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string Role = "civic_lens_public";
    private const string LoginPassword = "correction-view-test-password";
    private static readonly string ActionA = new('a', 64);
    private static readonly string ActionC = new('c', 64);
    private static readonly string ActionD = new('d', 64);
    private static readonly string ActionE = new('e', 64);
    private static readonly string DocumentChangeB = new('b', 32);
    private static readonly DateTimeOffset FirstPublishedAt = new DateTimeOffset(2026, 4, 5, 6, 7, 8, TimeSpan.Zero).AddTicks(47);
    private static readonly DateTimeOffset CorrectionOneAt = new DateTimeOffset(2026, 5, 6, 7, 8, 9, TimeSpan.FromMinutes(345)).AddTicks(61);
    private static readonly DateTimeOffset CorrectionTwoAt = new DateTimeOffset(2026, 5, 5, 7, 8, 9, TimeSpan.FromHours(-4)).AddTicks(23);
    private string connectionString = null!;
    private PostgresPublicationStore publications = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        publications = new PostgresPublicationStore(new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options), TestCodeVersion.Current);
        await ExecuteAsync($"ALTER ROLE {Role} LOGIN PASSWORD '{LoginPassword}'");
    }

    public async Task DisposeAsync()
    {
        await ExecuteAsync($"ALTER ROLE {Role} NOLOGIN PASSWORD NULL");
        await postgres.DropDatabaseAsync(connectionString);
    }

    [Fact]
    public async Task ViewsShowOnlyActiveExactBindingsAndPreserveValidatedBodies()
    {
        PublishedCorrection[] corrections =
        [
            Correction(3, CorrectionOneAt, "Corrected account", factsUnchanged: false),
            Correction(2, CorrectionTwoAt, null, factsUnchanged: true)
        ];
        var oldBinding = new PublishedRevisionBinding(PublishedRecordKind.Action, ActionA, 3);
        var currentAction = new PublishedRevisionBinding(PublishedRecordKind.Action, ActionA, 4);
        var currentDocumentChange = new PublishedRevisionBinding(PublishedRecordKind.DocumentChange, DocumentChangeB, 4);
        var emptyRecord = new PublishedRevisionBinding(PublishedRecordKind.Action, ActionC, 1);
        var staleRecord = new PublishedRevisionBinding(PublishedRecordKind.Action, ActionD, 4);
        var unpublishedRecord = new PublishedRevisionBinding(PublishedRecordKind.Action, ActionE, 1);
        var histories = await publications.PrepareCorrectionHistoriesAsync(
        [
            new(oldBinding, []),
            new(currentAction, corrections),
            new(currentDocumentChange, corrections),
            new(emptyRecord, []),
            new(staleRecord, corrections),
            new(unpublishedRecord, [])
        ], default);
        var historyByRecord = histories.ToDictionary(history => history.Record);
        Assert.Equal(historyByRecord[currentAction].HistoryId, historyByRecord[currentDocumentChange].HistoryId);
        Assert.Equal(historyByRecord[currentAction].HistoryId, historyByRecord[staleRecord].HistoryId);

        // These sparse published entries intentionally omit operational revision parents.
        // The fixture's explicit FK bypass is limited to this helper; IDs, entries, states, and histories satisfy their checks.
        await PublishedEntryFixtures.SeedEntriesAsync(connectionString,
        [
            PublishedEntryFixtures.Action(ActionA, FirstPublishedAt, 3),
            PublishedEntryFixtures.Action(ActionA, FirstPublishedAt, 4),
            PublishedEntryFixtures.DocumentChange(DocumentChangeB, 4, "Change record", FirstPublishedAt),
            PublishedEntryFixtures.Action(ActionC, FirstPublishedAt, 1),
            PublishedEntryFixtures.Action(ActionD, FirstPublishedAt, 1),
            PublishedEntryFixtures.Action(ActionD, FirstPublishedAt, 4),
            PublishedEntryFixtures.Action(ActionE, FirstPublishedAt, 1)
        ], allowMissingRevisionParents: true);
        await ExecuteAsync($"""
            INSERT INTO publication_releases (release_number, directory_name, actor_subject, published_at_utc_ticks, record_count, is_active) VALUES
              (1, '000001-00000000000000000000000000000001', 'test', 1, 1, false),
              (2, '000002-00000000000000000000000000000002', 'test', 2, 4, true);
            INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release) VALUES
              ('action', '{ActionA}', 1, 3, 1), ('action', '{ActionA}', 2, 4, NULL),
              ('documentChange', '{DocumentChangeB}', 2, 4, NULL),
              ('action', '{ActionC}', 2, 1, NULL), ('action', '{ActionD}', 2, 1, NULL);
            """);

        foreach (var (releaseNumber, binding) in new[]
        {
            (1, historyByRecord[oldBinding]),
            (2, historyByRecord[currentAction]),
            (2, historyByRecord[currentDocumentChange]),
            (2, historyByRecord[emptyRecord]),
            (2, historyByRecord[staleRecord]),
            (2, historyByRecord[unpublishedRecord])
        })
        {
            await ExecuteAsync("""
                INSERT INTO release_correction_histories (release_number, kind, record_id, revision_number, history_id)
                VALUES (@release_number, @kind, @record_id, @revision_number, @history_id)
                """, ("release_number", releaseNumber), ("kind", PublishedRecordReference.NameOf(binding.Record.Kind)),
                ("record_id", binding.Record.RecordId), ("revision_number", binding.Record.RevisionNumber), ("history_id", binding.HistoryId));
        }

        Assert.Equal(
            [
                $"2 action {ActionA} 4 {historyByRecord[currentAction].HistoryId} 1 2",
                $"2 action {ActionC} 1 {historyByRecord[emptyRecord].HistoryId} 1 0",
                $"2 documentChange {DocumentChangeB} 4 {historyByRecord[currentDocumentChange].HistoryId} 1 2"
            ],
            await PublicAsync("SELECT release_number || ' ' || kind || ' ' || record_id || ' ' || revision_number || ' ' || history_id || ' ' || encoding_version || ' ' || correction_count FROM public_api.record_correction_histories ORDER BY kind, record_id"));

        Assert.Equal(
            [
                $"action 0 3 {CorrectionOneAt.UtcTicks} 345 Corrected account false",
                $"action 1 2 {CorrectionTwoAt.UtcTicks} -240 none true",
                $"documentChange 0 3 {CorrectionOneAt.UtcTicks} 345 Corrected account false",
                $"documentChange 1 2 {CorrectionTwoAt.UtcTicks} -240 none true"
            ],
            await PublicAsync("SELECT kind || ' ' || position || ' ' || replaced_revision_number || ' ' || published_at_utc_ticks || ' ' || published_offset_minutes || ' ' || coalesce(note, 'none') || ' ' || facts_unchanged FROM public_api.record_correction_history_entries ORDER BY kind, position"));

        await ExecuteAsync("UPDATE publication_releases SET is_active = false; UPDATE publication_releases SET is_active = true WHERE release_number = 1");
        Assert.Equal(
            [$"1 action {ActionA} 3 {historyByRecord[oldBinding].HistoryId} 1 0"],
            await PublicAsync("SELECT release_number || ' ' || kind || ' ' || record_id || ' ' || revision_number || ' ' || history_id || ' ' || encoding_version || ' ' || correction_count FROM public_api.record_correction_histories"));
        Assert.Empty(await PublicAsync("SELECT record_id FROM public_api.record_correction_history_entries"));
    }

    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<string[]> PublicAsync(string sql)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { Username = Role, Password = LoginPassword };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync()) values.Add(Convert.ToString(reader.GetValue(0))!);
        return [.. values];
    }

    private static PublishedCorrection Correction(int replacedRevisionNumber, DateTimeOffset publishedAtUtc, string? note, bool factsUnchanged) => new()
    {
        ReplacedRevisionNumber = replacedRevisionNumber,
        PublishedAtUtc = publishedAtUtc,
        Note = note,
        FactsUnchanged = factsUnchanged
    };
}
