using CivicLens.Core.Review;
using CivicLens.Infrastructure.Collection.Migrations;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Votes;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace CivicLens.Tests.Infrastructure.PublicApi;

/// <summary>
/// The migration that gives a person-approved model statement its own AI label in both views that carry a statement label. The test first rolls back the
/// later migration <see cref="PressReleaseStatementLabel"/>, so the views hold the code label that this migration wrote.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PersonApprovedStatementLabelMigrationTests(PostgresCollection postgres) : IAsyncLifetime
{
    private static readonly string CodeApproved = new('5', 64);
    private static readonly string PersonApproved = new('6', 64);

    private const string FormerCodeLabel = "An AI summary of this release.";

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
        await ApplyAsync(new PressReleaseStatementLabel().DownOperations);
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(database);

    [Fact]
    public async Task ThePersonApprovedModelStatementKeepsItsAiLabelInBothViewsAndTheRollbackReturnsItsLabelToNullInBoth()
    {
        string[] withLabel = [$"Code approved|{FormerCodeLabel}", $"Person approved|{AiLabelWords.StatementCheckedByPersonWording}"];
        string[] withoutLabel = [$"Code approved|{FormerCodeLabel}", "Person approved|null"];
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

    private Task<string[]> LabelsAsync(string source) =>
        ViewsSeed.QueryAsPublicAsync(database, $"SELECT headline || '|' || coalesce(label, 'null') FROM public_api.{source} ORDER BY 1");

    private async Task ApplyAsync(IReadOnlyList<MigrationOperation> operations)
    {
        foreach (var operation in operations) await ViewsSeed.ExecuteAsync(database, Assert.IsType<SqlOperation>(operation).Sql);
    }
}
