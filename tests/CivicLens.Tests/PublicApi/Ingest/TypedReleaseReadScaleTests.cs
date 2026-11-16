using System.Diagnostics;
using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Ingest;
using CivicLens.PublicApi.Records;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Votes;
using Npgsql;
using Xunit.Abstractions;

namespace CivicLens.Tests.PublicApi.Ingest;

/// <summary>
/// The full read of a production-size release stays inside the statement timeout of <c>civic_lens_public</c> for every statement.
/// Each query reads at most <see cref="RevisionKeys.MaxCount"/> keys. It starts from the keys and reads its view for each key with <see cref="RevisionKeys.Match"/>.
/// A read of every key in one statement, a plain <c>IN</c> filter, or a plain join to <c>release_officials</c> fails at the timeout.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class TypedReleaseReadScaleTests(PostgresCollection postgres, ITestOutputHelper output)
{
    [Fact]
    public async Task EveryRecordOfTheProductionSizeOfRelease1096WithBillsVotesStatementsAndExecutiveActionsReadsInsideTheTimeoutOfThePublicRole()
    {
        var templates = ProductionSizeRelease.Templates();
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await new TypedReleaseSite(database).PublishAsync(1, templates);
            await ViewsSeed.ExecuteAsync(database, ProductionSizeRelease.CosponsorsSql(templates.Single(record => record.Entry.Kind == PublishedRecordKind.Action).Entry.RecordId));
            await using var views = NpgsqlDataSource.Create(await ApiHost.LogInPublicRoleAsync(database));
            var expected = (await ReadAsync(views)).Records.ToDictionary(record => record.Entry.Kind, Project);
            await ProductionSizeRelease.CopyAsync(database, templates);

            var clock = Stopwatch.StartNew();
            var batch = await ReadAsync(views);
            output.WriteLine($"{batch.Records.Count} revisions read in {clock.ElapsedMilliseconds} ms.");

            Assert.Empty(batch.Failures);
            Assert.Equal(templates.Length + ProductionSizeRelease.Actions + ProductionSizeRelease.Votes + ProductionSizeRelease.Statements + ProductionSizeRelease.ExecutiveActions, batch.Records.Count);
            Assert.Equal(ProductionSizeRelease.Actions + 1, batch.Records.Count(record => record.Entry.Kind == PublishedRecordKind.Action));
            Assert.All(batch.Records, record => Assert.Equal(expected[record.Entry.Kind], Project(record)));
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }

    private static async Task<RecordBatch> ReadAsync(NpgsqlDataSource views)
    {
        await using var read = (await new TypedReleaseSource(new RecordSnapshot(views)).ReadActiveAsync(CancellationToken.None))!;
        return await read.ReadRecordsAsync(read.Release.Records, CancellationToken.None);
    }

    // Every field that the index stores for the record, without its record ID.
    private static string Project(IndexedRecord record)
    {
        var entry = record.Entry;
        return string.Join('\u001f', entry.Kind, entry.RevisionNumber, entry.Headline, entry.FirstPublishedAtUtc.UtcTicks,
            string.Join(',', entry.Officials.Select(official => official.Id + "=" + official.Name)), entry.MatchedOfficialId, string.Join(',', entry.IssueIds),
            string.Join(',', entry.PolicyAreaIds), entry.SourceHost, entry.EventDate, entry.BillLine, entry.BillTitle, record.Summary, record.Source, record.SnippetText);
    }

}
