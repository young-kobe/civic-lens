using CivicLens.Infrastructure.Collection;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Votes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Publication;

/// <summary>The migration that replaces facts_unchanged with text_rewrite. Every vote rewrite before it was model text, so true becomes Model.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class CorrectionTextRewriteMigrationTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string Migration = "20261115004418_CorrectionTextRewrite";
    private static readonly string History = new('a', 64);
    private static readonly string Record = new('b', 64);
    private string connectionString = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateEmptyDatabaseAsync();
        await MigrateToAsync(Before());
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task TheBackfillTurnsATrueMarkIntoAModelRewriteAndEveryOtherEntryIntoNoRewriteInBothTablesAndTheirViews()
    {
        await ViewsSeed.ExecuteAsync(connectionString, $"""
            SET session_replication_role = replica;
            INSERT INTO published_correction_histories (history_id, encoding_version, correction_count) VALUES ('{History}', 1, 3);
            INSERT INTO published_correction_history_entries
                (history_id, "position", replaced_revision_number, published_at_utc_ticks, published_offset_minutes, note, facts_unchanged) VALUES
                ('{History}', 0, 4, 0, 0, NULL, true), ('{History}', 1, 3, 0, 0, NULL, false), ('{History}', 2, 2, 0, 0, 'Fixed.', false);
            INSERT INTO publication_withdrawal_corrections
                (kind, record_id, first_release, "position", replaced_revision_number, published_at_utc_ticks, note, facts_unchanged) VALUES
                ('vote', '{Record}', 1, 0, 3, 0, NULL, true), ('vote', '{Record}', 1, 1, 2, 0, 'Fixed.', false);
            """);

        await MigrateToAsync(Migration);

        Assert.Equal(["0 Model", "1 None", "2 None"], await ViewsSeed.QueryAsync(connectionString,
            "SELECT position || ' ' || text_rewrite FROM published_correction_history_entries ORDER BY position"));
        Assert.Equal(["0 Model", "1 None"], await ViewsSeed.QueryAsync(connectionString,
            "SELECT position || ' ' || text_rewrite FROM publication_withdrawal_corrections ORDER BY position"));
        Assert.Equal(["record_correction_history_entries", "withdrawal_corrections"], await ViewsSeed.QueryAsync(connectionString, """
            SELECT table_name::text FROM information_schema.columns
             WHERE table_schema = 'public_api' AND column_name = 'text_rewrite' ORDER BY table_name
            """));
        Assert.Empty(await ViewsSeed.QueryAsync(connectionString,
            "SELECT table_name::text FROM information_schema.columns WHERE column_name = 'facts_unchanged'"));
    }

    [Fact]
    public async Task TheRollbackRefusesACodeRewriteBecauseTheBoolCannotHoldItAndOtherwiseRestoresTheMark()
    {
        await MigrateToAsync(Migration);
        await ViewsSeed.ExecuteAsync(connectionString, $"""
            SET session_replication_role = replica;
            INSERT INTO published_correction_histories (history_id, encoding_version, correction_count) VALUES ('{History}', 1, 2);
            INSERT INTO published_correction_history_entries
                (history_id, "position", replaced_revision_number, published_at_utc_ticks, published_offset_minutes, note, text_rewrite) VALUES
                ('{History}', 0, 3, 0, 0, NULL, 'Code'), ('{History}', 1, 2, 0, 0, NULL, 'Model');
            """);

        await Assert.ThrowsAsync<PostgresException>(() => MigrateToAsync(Before()));
        await ViewsSeed.ExecuteAsync(connectionString, "UPDATE published_correction_history_entries SET text_rewrite = 'None' WHERE position = 0");
        await MigrateToAsync(Before());

        Assert.Equal(["0 false", "1 true"], await ViewsSeed.QueryAsync(connectionString,
            "SELECT position || ' ' || facts_unchanged FROM published_correction_history_entries ORDER BY position"));
        Assert.Equal(["record_correction_history_entries", "withdrawal_corrections"], await ViewsSeed.QueryAsync(connectionString, """
            SELECT table_name::text FROM information_schema.columns
             WHERE table_schema = 'public_api' AND column_name = 'facts_unchanged' ORDER BY table_name
            """));
    }

    private string Before()
    {
        using var db = NewContext();
        return db.Database.GetMigrations().TakeWhile(id => id != Migration).Last();
    }

    private async Task MigrateToAsync(string migration)
    {
        await using var db = NewContext();
        await db.GetService<IMigrator>().MigrateAsync(migration);
    }

    private CollectionAttemptDbContext NewContext() =>
        new(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
}
