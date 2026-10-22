using CivicLens.Tests.Fixtures;
using System.Text.Json;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Collection.Contracts;
using CivicLens.Infrastructure.Collection;
using CivicLens.Tests.Application.Collection.Bills;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Collection;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class JobSourceColumnsMigrationTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string PreviousMigration = "20261022160000_DerivedJobCharges";
    private const string PageUrl = "https://example.test/page";
    private const string EarlierPageUrl = "https://example.test/page-earlier";
    private const string OtherSourceUrl = "https://example.test/other";
    private static readonly DateOnly Backfill = new(2025, 1, 3);
    private string connectionString = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateEmptyDatabaseAsync();
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    // Older jobs keep the source and URL only inside their JSON. The scheduler and source health read the new columns,
    // so the backfill must give each older job the root status that the writer gives a new job.
    [Fact]
    public async Task OlderJobsGetTheSourceAndRootStatusThatTheWriterGivesNewJobs()
    {
        await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>()
            .UseNpgsql(connectionString).Options);
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        var page = CollectionFixtures.Definition(PageConfiguration(PageUrl), "page", Backfill);
        var earlier = CollectionFixtures.Definition(PageConfiguration(EarlierPageUrl), "page", Backfill);
        var bills = new ConfiguredCollectionSource(CongressBillSourceConfigurationTests.Configuration(), "congress-bills", Backfill);
        var jobs = new Dictionary<string, CollectionJobDefinition>
        {
            ["root"] = page,
            ["article"] = page with { Mode = CollectionMode.Page, Url = "https://example.test/page/article" },
            ["other-source-url"] = page with { Url = OtherSourceUrl },
            ["earlier-revision-root"] = earlier,
            ["earlier-url-in-later-revision"] = page with { Url = EarlierPageUrl },
            ["legacy"] = page with { ConfigurationRevision = null, CoverageAsOf = null },
            ["bill-list"] = bills.CreateApiJobDefinition(ApiResource.BillList,
                new BillStreamWindow(Backfill, Backfill).RequestUrl(bills.SourceUrl), Backfill),
            ["bill-detail"] = bills.CreateApiJobDefinition(ApiResource.Bill,
                "https://api.congress.gov/v3/bill/119/hr/1?format=json", Backfill)
        };
        foreach (var (jobId, definition) in jobs) await InsertAsync(jobId, definition);

        await db.Database.MigrateAsync();

        var columns = await ReadColumnsAsync();
        Assert.Equal(new Dictionary<string, (string, bool)>
        {
            ["root"] = ("page", true),
            ["article"] = ("page", false),
            ["other-source-url"] = ("page", false),
            ["earlier-revision-root"] = ("page", true),
            ["earlier-url-in-later-revision"] = ("page", false),
            ["legacy"] = ("page", false),
            ["bill-list"] = ("congress-bills", true),
            ["bill-detail"] = ("congress-bills", false)
        }, columns);
        var bound = jobs.Where(job => job.Value.ConfigurationRevision is not null).ToDictionary();
        Assert.Equal(bound.ToDictionary(job => job.Key, job => job.Value.IsRoot()),
            columns.Where(row => bound.ContainsKey(row.Key)).ToDictionary(row => row.Key, row => row.Value.Item2));
    }

    private static CollectionConfiguration PageConfiguration(string pageUrl) => new()
    {
        People = [new PersonConfiguration { Id = "person", Name = "Person" }],
        Sources = [Source("page", pageUrl), Source("other", OtherSourceUrl)]
    };

    private static WatchedSourceConfiguration Source(string id, string url) => new()
    {
        Id = id,
        Mode = CollectionMode.Html,
        Coverage = [new SourceCoverageConfiguration { PersonId = "person" }],
        Url = url,
        AllowedOrigin = "https://example.test",
        AllowedPathPrefix = "/"
    };

    private async Task InsertAsync(string jobId, CollectionJobDefinition definition)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO collection_configuration_revisions (revision_id, json, created_at)
            SELECT @revision, @revision_json, 0 WHERE @revision IS NOT NULL
            ON CONFLICT (revision_id) DO NOTHING;
            INSERT INTO collection_jobs (job_id, idempotency_key, definition_json, configuration_revision_id, state, created_at,
                cancellation_requested, lease_fence)
            VALUES (@job, @job, @definition, @revision, 'Pending', 1, false, 0);
            """, connection);
        command.Parameters.AddWithValue("job", jobId);
        command.Parameters.AddWithValue("definition", JsonSerializer.Serialize(
            definition with { ConfigurationRevision = null }, CollectionProtocol.JsonOptions));
        command.Parameters.Add(new NpgsqlParameter<string?>("revision", definition.ConfigurationRevision?.Id));
        command.Parameters.Add(new NpgsqlParameter<string?>("revision_json", definition.ConfigurationRevision?.Json));
        await command.ExecuteNonQueryAsync();
    }

    private async Task<Dictionary<string, (string, bool)>> ReadColumnsAsync()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT job_id, source_id, is_root FROM collection_jobs", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new Dictionary<string, (string, bool)>();
        while (await reader.ReadAsync())
            rows.Add(reader.GetString(0), (reader.GetString(1), reader.GetBoolean(2)));
        return rows;
    }
}
