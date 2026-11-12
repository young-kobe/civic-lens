using CivicLens.Core.Review;
using CivicLens.Infrastructure.Collection.Migrations;
using CivicLens.PublicWeb.Publication;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Votes;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.PublicApi;

/// <summary>The migration that gives a person-approved model statement its own AI label in both views that carry a statement label.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PersonApprovedStatementLabelMigrationTests(PostgresCollection postgres) : IAsyncLifetime
{
    private static readonly string CodeApproved = new('5', 64);
    private static readonly string PersonApproved = new('6', 64);

    private string database = null!;

    public async Task InitializeAsync()
    {
        database = await postgres.CreateMigratedDatabaseAsync();
        await ViewsSeed.ExecuteAsync(database, $"""
            SET session_replication_role = replica;
            {PublicRecordPageRows.ActiveRelease}
            INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release) VALUES
              ('statement', '{CodeApproved}', 1, 1, NULL), ('statement', '{PersonApproved}', 1, 1, NULL);
            {PublicRecordPageRows.Headline("statement", CodeApproved, "Code approved", "Model")}
            {PublicRecordPageRows.StatementApproval(CodeApproved, 1, 1, ReviewAuthor.CodeSubject)}
            {PublicRecordPageRows.Headline("statement", PersonApproved, "Person approved", "Model", "Person")}
            {PublicRecordPageRows.StatementApproval(PersonApproved, 1, 1, "auth0|owner")}
            INSERT INTO statement_revision_speakers (candidate_id, revision_number, official_id, position)
              VALUES ('{CodeApproved}', 1, 'official', 0), ('{PersonApproved}', 1, 'official', 0);
            """);
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(database);

    [Fact]
    public async Task ThePersonApprovedModelStatementKeepsItsAiLabelInBothViewsAndTheRollbackReturnsItsLabelToNullInBoth()
    {
        string[] withLabel = [$"Code approved|{PublishedRecordLabels.StatementModelWording}", $"Person approved|{PublishedRecordLabels.StatementCheckedByPersonWording}"];
        string[] withoutLabel = [$"Code approved|{PublishedRecordLabels.StatementModelWording}", "Person approved|null"];
        Assert.Equal(withLabel, await LabelsAsync("record_headlines WHERE kind = 'statement'"));
        Assert.Equal(withLabel, await LabelsAsync("statements"));

        var migration = new PersonApprovedStatementLabel();
        await ApplyAsync(migration.DownOperations);
        Assert.Equal(withoutLabel, await LabelsAsync("record_headlines WHERE kind = 'statement'"));
        Assert.Equal(withoutLabel, await LabelsAsync("statements"));

        await ApplyAsync(migration.UpOperations);
        Assert.Equal(withLabel, await LabelsAsync("record_headlines WHERE kind = 'statement'"));
        Assert.Equal(withLabel, await LabelsAsync("statements"));
    }

    private async Task<string[]> LabelsAsync(string source)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using (var role = new NpgsqlCommand("SET ROLE civic_lens_public", connection)) await role.ExecuteNonQueryAsync();
        await using var command = new NpgsqlCommand($"SELECT headline || '|' || coalesce(label, 'null') FROM public_api.{source} ORDER BY 1", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync()) rows.Add(reader.GetString(0));
        return [.. rows];
    }

    private async Task ApplyAsync(IReadOnlyList<MigrationOperation> operations)
    {
        foreach (var operation in operations) await ViewsSeed.ExecuteAsync(database, Assert.IsType<SqlOperation>(operation).Sql);
    }
}
