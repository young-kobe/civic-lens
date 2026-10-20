using System.Net;
using System.Threading.RateLimiting;
using CivicLens.PublicApi.Http;
using CivicLens.PublicApi.Ingest;
using CivicLens.PublicApi.Schema;
using CivicLens.PublicApi.Search;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CivicLens.PublicApi;

public static class PublicApiApp
{
    /// <summary>Builds the service. A test passes a handler that serves the release files in place of the network.</summary>
    public static WebApplication Build(PublicApiOptions options, string[] args, HttpMessageHandler? releaseHandler = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        var writer = NpgsqlDataSource.Create(options.Database);
        var reader = NpgsqlDataSource.Create(options.ReaderDatabase);
        var feed = new ReleaseFeed(new HttpClient(releaseHandler ?? new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All })
        {
            BaseAddress = options.ReleaseBaseUrl,
            MaxResponseContentBufferSize = ReleaseFeed.MaximumRecordBytes,
            Timeout = TimeSpan.FromMinutes(1)
        });

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(new SchemaMigrator(writer));
        builder.Services.AddSingleton(new ReleaseIngest(feed, writer));
        builder.Services.AddSingleton(new SearchIndex(reader));
        builder.Services.AddHostedService<SchemaStartup>();
        builder.Services.AddHostedService<IngestWorker>();
        var clients = new ClientAddress(options.TrustedProxyRanges);
        builder.Services.AddRateLimiter(limits =>
        {
            limits.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limits.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(clients.Of(context), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = options.RequestsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));
        });

        var app = builder.Build();
        app.Lifetime.ApplicationStopped.Register(() =>
        {
            writer.Dispose();
            reader.Dispose();
        });
        app.Use((context, next) =>
        {
            context.Response.Headers.AccessControlAllowOrigin = "*";
            return next(context);
        });
        app.UseRateLimiter();
        app.MapSearch();
        return app;
    }
}
