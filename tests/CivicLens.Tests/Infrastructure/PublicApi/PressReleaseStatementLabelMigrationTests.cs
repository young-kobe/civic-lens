using CivicLens.Core.Review;
using CivicLens.Infrastructure.Collection.Migrations;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Votes;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace CivicLens.Tests.Infrastructure.PublicApi;

/// <summary>The migration that gives a code-approved model statement the label "An AI summary of this press release." in both views that carry a statement label.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PressReleaseStatementLabelMigrationTests(PostgresCollection postgres) : IAsyncLifetime
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
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(database);

    [Fact]
    public async Task ACodeApprovedModelStatementReadsPressReleaseInBothViewsAndTheRollbackReturnsTheFormerLabelInBoth()
    {
        string[] pressRelease = [$"Code approved|{AiLabelWords.StatementModelWording}", $"Person approved|{AiLabelWords.StatementCheckedByPersonWording}"];
        string[] former = [$"Code approved|{FormerCodeLabel}", $"Person approved|{AiLabelWords.StatementCheckedByPersonWording}"];
        Assert.Equal("An AI summary of this press release.", AiLabelWords.StatementModelWording);
        Assert.Equal(pressRelease, await LabelsAsync("record_headlines WHERE kind = 'statement'"));
        Assert.Equal(pressRelease, await LabelsAsync("statements"));

        var migration = new PressReleaseStatementLabel();
        await ApplyAsync(migration.DownOperations);
        Assert.Equal(former, await LabelsAsync("record_headlines WHERE kind = 'statement'"));
        Assert.Equal(former, await LabelsAsync("statements"));

        await ApplyAsync(migration.UpOperations);
        Assert.Equal(pressRelease, await LabelsAsync("record_headlines WHERE kind = 'statement'"));
        Assert.Equal(pressRelease, await LabelsAsync("statements"));
    }

    private Task<string[]> LabelsAsync(string source) =>
        ViewsSeed.QueryAsPublicAsync(database, $"SELECT headline || '|' || coalesce(label, 'null') FROM public_api.{source} ORDER BY 1");

    private async Task ApplyAsync(IReadOnlyList<MigrationOperation> operations)
    {
        foreach (var operation in operations) await ViewsSeed.ExecuteAsync(database, Assert.IsType<SqlOperation>(operation).Sql);
    }
}
