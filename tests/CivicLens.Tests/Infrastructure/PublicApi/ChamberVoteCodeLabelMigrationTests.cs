using CivicLens.Infrastructure.Collection.Migrations;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Votes;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace CivicLens.Tests.Infrastructure.PublicApi;

/// <summary>The migration that gives a Senate vote with code text the Senate label in the three views that carry a vote label. A House vote and a model vote keep their labels.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class ChamberVoteCodeLabelMigrationTests(PostgresCollection postgres) : IAsyncLifetime
{
    private static readonly SeededVote[] Votes =
    [
        new('a', "2025-03-01") { Casts = ["Yea"], PersonIds = ["house-code"], TextOrigin = "Code" },
        new('b', "2025-03-02") { Casts = ["Yea"], PersonIds = ["senate-code"], TextOrigin = "Code", Chamber = "Senate" },
        new('c', "2025-03-03") { Casts = ["Yea"], PersonIds = ["senate-model"], TextOrigin = "Model", Chamber = "Senate" }
    ];

    private static readonly string[] ByChamber =
        [$"Headline a|{AiLabelWords.CodeWording}", $"Headline b|{AiLabelWords.SenateCodeWording}", $"Headline c|{AiLabelWords.ModelWording}"];

    private static readonly string[] HouseText =
        [$"Headline a|{AiLabelWords.CodeWording}", $"Headline b|{AiLabelWords.CodeWording}", $"Headline c|{AiLabelWords.ModelWording}"];

    private string database = null!;

    public async Task InitializeAsync()
    {
        database = await postgres.CreateMigratedDatabaseAsync();
        await ViewsSeed.ActivateReleaseAsync(database, 1, DateTimeOffset.UnixEpoch, recordCount: Votes.Length);
        await ViewsSeed.AddVotesAsync(database, Votes);
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(database);

    [Fact]
    public async Task ASenateVoteWithCodeTextReadsTheSenateLabelInEveryViewAndTheRollbackReturnsTheHouseLabel()
    {
        await AssertLabelsAsync(ByChamber);

        var migration = new ChamberVoteCodeLabel();
        await ApplyAsync(migration.DownOperations);
        await AssertLabelsAsync(HouseText);

        await ApplyAsync(migration.UpOperations);
        await AssertLabelsAsync(ByChamber);
    }

    private async Task AssertLabelsAsync(string[] expected)
    {
        Assert.Equal(expected, await LabelsAsync("votes"));
        Assert.Equal(expected, await LabelsAsync("member_votes"));
        Assert.Equal(expected, await LabelsAsync("record_headlines WHERE kind = 'vote'"));
    }

    private Task<string[]> LabelsAsync(string source) =>
        ViewsSeed.QueryAsPublicAsync(database, $"SELECT headline || '|' || label FROM public_api.{source} ORDER BY 1");

    private async Task ApplyAsync(IReadOnlyList<MigrationOperation> operations)
    {
        foreach (var operation in operations) await ViewsSeed.ExecuteAsync(database, Assert.IsType<SqlOperation>(operation).Sql);
    }
}
