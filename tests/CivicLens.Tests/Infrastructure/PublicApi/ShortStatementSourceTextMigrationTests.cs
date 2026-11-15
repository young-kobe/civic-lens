using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Votes;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.PublicApi;

/// <summary>The migration that lets code write the source text of a short release as a statement revision, and labels it in both views.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class ShortStatementSourceTextMigrationTests(PostgresCollection postgres) : IAsyncLifetime
{
    private static readonly string SourceText = new('7', 64);
    private static readonly string ModelSummary = new('8', 64);

    private string database = null!;

    public async Task InitializeAsync()
    {
        database = await postgres.CreateMigratedDatabaseAsync();
        await ViewsSeed.ExecuteAsync(database, $"""
            SET session_replication_role = replica;
            {PublicRecordPageRows.ActiveRelease}
            INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release) VALUES
              ('statement', '{SourceText}', 1, 1, NULL), ('statement', '{ModelSummary}', 1, 1, NULL);
            {PublicRecordPageRows.Headline("statement", SourceText, "Source text", "Code")}
            {PublicRecordPageRows.Headline("statement", ModelSummary, "Model summary", "Model")}
            INSERT INTO statement_revision_speakers (candidate_id, revision_number, official_id, position)
              VALUES ('{SourceText}', 1, 'official', 0), ('{ModelSummary}', 1, 'official', 0);
            """);
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(database);

    [Fact]
    public async Task ACodeRevisionReadsAsCodeTextWithTheSourceTextLabelInBothViewsAndAModelSummaryKeepsItsAiLabel()
    {
        string[] expected = [$"Model summary|Model|{AiLabelWords.StatementModelWording}", $"Source text|Code|{AiLabelWords.StatementSourceTextWording}"];
        Assert.Equal("The source text of this press release, with no AI.", AiLabelWords.StatementSourceTextWording);
        Assert.Equal(expected, await RowsAsync("record_headlines WHERE kind = 'statement'"));
        Assert.Equal(expected, await RowsAsync("statements"));
    }

    [Theory]
    [InlineData("statement_date = '2026-10-01', statement_date_evidence_extraction_id = 'e', statement_date_evidence_start = 0, statement_date_evidence_length = 1",
        "ck_statement_revisions_code_date")]
    [InlineData("run_id = NULL", "ck_statement_revisions_run")]
    public async Task TheDatabaseRefusesACodeRevisionWithADateOrWithoutARunBecauseCodeNeverGuessesADateAndAlwaysKeepsItsRun(string change, string check)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() => ViewsSeed.ExecuteAsync(database,
            $"SET session_replication_role = replica; UPDATE statement_revisions SET {change} WHERE candidate_id = '{SourceText}';"));

        Assert.Equal(check, error.ConstraintName);
    }

    private Task<string[]> RowsAsync(string source) => ViewsSeed.QueryAsPublicAsync(database,
        $"SELECT headline || '|' || text_origin || '|' || coalesce(label, 'null') FROM public_api.{source} ORDER BY 1");
}
