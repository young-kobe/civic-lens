using CivicLens.PublicApi;
using CivicLens.PublicApi.Ingest;
using CivicLens.PublicApi.Reports;
using CivicLens.PublicApi.Schema;
using CivicLens.PublicApi.Search;
using CivicLens.Tests.Infrastructure.Collection;
using Npgsql;

namespace CivicLens.Tests.PublicApi;

/// <summary>An empty search database with its schema applied, a fake release site, and the ingest and the index over them.</summary>
internal sealed class SearchWorld : IAsyncDisposable
{
    private readonly PostgresCollection postgres;
    private readonly NpgsqlDataSource writer;
    private readonly NpgsqlDataSource reader;

    private SearchWorld(PostgresCollection postgres, PublicApiOptions options)
    {
        this.postgres = postgres;
        Options = options;
        writer = NpgsqlDataSource.Create(options.Database);
        reader = NpgsqlDataSource.Create(options.ReaderDatabase);
        Ingest = new ReleaseIngest(new ReleaseFeed(new HttpClient(Site) { BaseAddress = options.ReleaseBaseUrl }), writer);
        Index = new SearchIndex(reader);
        Reports = new ReportStore(writer, options);
    }

    public PublicApiOptions Options { get; }
    public FakeReleaseSite Site { get; } = new();
    public ReleaseIngest Ingest { get; }
    public SearchIndex Index { get; }
    public ReportStore Reports { get; }
    public NpgsqlDataSource Database => writer;

    public static async Task<SearchWorld> CreateAsync(PostgresCollection postgres, TimeSpan? queryTimeout = null)
    {
        var connection = await postgres.CreateEmptyDatabaseAsync();
        var world = new SearchWorld(postgres, new PublicApiOptions
        {
            ReleaseBaseUrl = PublicApiOptions.Directory("http://release.test"),
            Database = connection,
            QueryTimeout = queryTimeout ?? TimeSpan.FromSeconds(2)
        });
        await new SchemaMigrator(world.writer).ApplyAsync(CancellationToken.None);
        return world;
    }

    public Task<IngestResult> IngestAsync() => Ingest.RunAsync(CancellationToken.None);

    public async Task<IngestResult> PublishAndIngestAsync(int number, params SampleRecord[] records)
    {
        Site.Publish(number, records);
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
        await postgres.DropDatabaseAsync(Options.Database);
    }
}
