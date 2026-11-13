using CivicLens.PublicApi;
using CivicLens.PublicApi.Ingest;
using CivicLens.PublicApi.Records;
using CivicLens.PublicApi.Reports;
using CivicLens.PublicApi.Schema;
using CivicLens.PublicApi.Search;
using CivicLens.Tests.Infrastructure.Collection;
using Npgsql;

namespace CivicLens.Tests.PublicApi;

/// <summary>
/// An empty search database with its schema applied, a migrated operational database that <see cref="Site"/> publishes into,
/// and the ingest and the index over them. The ingest reads the <c>public_api</c> views as the role <c>civic_lens_public</c>, as the service does.
/// </summary>
internal sealed class SearchWorld : IAsyncDisposable
{
    private readonly PostgresCollection postgres;
    private readonly NpgsqlDataSource writer;
    private readonly NpgsqlDataSource reader;
    private readonly NpgsqlDataSource views;

    private SearchWorld(PostgresCollection postgres, PublicApiOptions options, string operationalDatabase)
    {
        this.postgres = postgres;
        Options = options;
        OperationalDatabase = operationalDatabase;
        writer = NpgsqlDataSource.Create(options.Database);
        reader = NpgsqlDataSource.Create(options.ReaderDatabase);
        views = NpgsqlDataSource.Create(options.PublicViewsReaderDatabase);
        Site = new TypedReleaseSite(operationalDatabase);
        Ingest = new ReleaseIngest(new TypedReleaseSource(new RecordSnapshot(views)), writer);
        Index = new SearchIndex(reader);
        Reports = new ReportStore(writer, options);
    }

    public PublicApiOptions Options { get; }
    /// <summary>The operational database as its owner, for a test to change the rows that the views show.</summary>
    public string OperationalDatabase { get; }
    public TypedReleaseSite Site { get; }
    public ReleaseIngest Ingest { get; }
    public SearchIndex Index { get; }
    public ReportStore Reports { get; }
    public NpgsqlDataSource Database => writer;

    public static async Task<SearchWorld> CreateAsync(PostgresCollection postgres, TimeSpan? queryTimeout = null)
    {
        var operational = await postgres.CreateMigratedDatabaseAsync();
        var world = new SearchWorld(postgres, new PublicApiOptions
        {
            Database = await postgres.CreateEmptyDatabaseAsync(),
            PublicViewsDatabase = await ApiHost.LogInPublicRoleAsync(operational),
            QueryTimeout = queryTimeout ?? TimeSpan.FromSeconds(2)
        }, operational);
        await new SchemaMigrator(world.writer).ApplyAsync(CancellationToken.None);
        return world;
    }

    public Task<IngestResult> IngestAsync() => Ingest.RunAsync(CancellationToken.None);

    public async Task<IngestResult> PublishAndIngestAsync(int number, params SampleRecord[] records)
    {
        await Site.PublishAsync(number, records);
        return await IngestAsync();
    }

    public async Task<SearchPage> SearchAsync(Func<SearchRequest, SearchRequest>? change = null, int? release = null)
    {
        var request = change?.Invoke(new SearchRequest()) ?? new SearchRequest();
        var found = await Index.FindReleaseAsync(release ?? request.Release, CancellationToken.None) ?? throw new InvalidOperationException("No release.");
        return await Index.SearchAsync(found.ReleaseNumber, request, CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        await writer.DisposeAsync();
        await reader.DisposeAsync();
        await views.DisposeAsync();
        await postgres.DropDatabaseAsync(Options.Database);
        await ApiHost.LogOutPublicRoleAsync(OperationalDatabase);
        await postgres.DropDatabaseAsync(OperationalDatabase);
    }
}
