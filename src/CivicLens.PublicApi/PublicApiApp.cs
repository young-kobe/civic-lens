using System.Net;
using System.Threading.RateLimiting;
using CivicLens.PublicApi.ActiveRelease;
using CivicLens.PublicApi.Bills;
using CivicLens.PublicApi.Boards;
using CivicLens.PublicApi.Briefs;
using CivicLens.PublicApi.ExecutiveActions;
using CivicLens.PublicApi.DocumentChanges;
using CivicLens.PublicApi.Home;
using CivicLens.PublicApi.Http;
using CivicLens.PublicApi.Ingest;
using CivicLens.PublicApi.Issues;
using CivicLens.PublicApi.Officials;
using CivicLens.PublicApi.Pages;
using CivicLens.PublicApi.PartyAgreement;
using CivicLens.PublicApi.Records;
using CivicLens.PublicApi.Reports;
using CivicLens.PublicApi.Schema;
using CivicLens.PublicApi.Search;
using CivicLens.PublicApi.Statements;
using CivicLens.PublicApi.Votes;
using CivicLens.PublicApi.ZipDistricts;
using CivicLens.PublicWeb.Publication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CivicLens.PublicApi;

public static class PublicApiApp
{
    /// <summary>Builds the service. A test passes a handler that serves the release files in place of the network, one that answers the bot check, and a log provider.
    /// The service lowers the request logs of ASP.NET Core to warnings, because an information log holds the request address with the search words.</summary>
    public static WebApplication Build(PublicApiOptions options, string[] args, HttpMessageHandler? releaseHandler = null,
        HttpMessageHandler? turnstileHandler = null, ILoggerProvider? logProvider = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
        if (logProvider is not null) builder.Logging.AddProvider(logProvider);
        var buildId = options.PublicBaseUrl is null ? null : BuildId.FromAssembly(typeof(PublicApiApp).Assembly);
        var writer = NpgsqlDataSource.Create(options.Database);
        var reader = NpgsqlDataSource.Create(options.ReaderDatabase);
        var views = NpgsqlDataSource.Create(options.PublicViewsReaderDatabase);
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
        builder.Services.AddSingleton(new ActiveReleaseReader(views));
        builder.Services.AddSingleton(new VoteStore(views));
        builder.Services.AddSingleton(provider => new VotePageReader(provider.GetRequiredService<VoteStore>(), views));
        builder.Services.AddSingleton(new PartyAgreementStore(views));
        builder.Services.AddSingleton(provider => new BoardStore(views, provider.GetRequiredService<PartyAgreementStore>()));
        builder.Services.AddSingleton(new MemberVoteStore(views));
        builder.Services.AddSingleton(new BillStore(views));
        builder.Services.AddSingleton(new RecordKindStore(views));
        builder.Services.AddSingleton(new RecordSnapshot(views));
        builder.Services.AddSingleton(new StatementStore(views));
        builder.Services.AddSingleton(provider => new StatementPageReader(provider.GetRequiredService<StatementStore>(), provider.GetRequiredService<RecordSnapshot>()));
        builder.Services.AddSingleton(new DocumentChangeStore(views));
        builder.Services.AddSingleton(provider => new DocumentChangePageReader(provider.GetRequiredService<DocumentChangeStore>(), provider.GetRequiredService<RecordSnapshot>()));
        builder.Services.AddSingleton(new BriefStore(views));
        builder.Services.AddSingleton(new ZipDistrictStore(views));
        builder.Services.AddSingleton(new IssueStore(views));
        builder.Services.AddSingleton(new HomeStore(views));
        builder.Services.AddSingleton(provider => new HomePageReader(provider.GetRequiredService<HomeStore>(), options.BrowserApiUrl));
        builder.Services.AddSingleton(new RelatedStore(views));
        builder.Services.AddSingleton(new WithdrawalStore(views));
        builder.Services.AddSingleton(new ExecutiveActionStore(views));
        builder.Services.AddSingleton(provider => new ExecutiveActionPageReader(provider.GetRequiredService<ExecutiveActionStore>()));
        builder.Services.AddSingleton<PageRenderer>();
        if (options.PublicBaseUrl is { } baseUrl)
        {
            builder.Services.AddSingleton(baseUrl);
            builder.Services.AddSingleton(provider => new MembersStripSlot
            {
                BrowserApiRoot = provider.GetRequiredService<HomePageReader>().BrowserApiRoot
            });
            builder.Services.AddSingleton(new PageCache(buildId!, baseUrl, options.BrowserApiUrl));
            builder.Services.AddSingleton<ServerPageResponder>();
        }
        builder.Services.AddSingleton(new OfficialStore(views));
        builder.Services.AddSingleton(new ReportStore(writer, options));
        if (options.Turnstile is { } turnstile)
            builder.Services.AddSingleton(new TurnstileVerifier(
                new HttpClient(turnstileHandler ?? new SocketsHttpHandler()) { Timeout = TurnstileVerifier.Timeout }, turnstile));
        builder.Services.AddHostedService<SchemaStartup>();
        builder.Services.AddHostedService<IngestWorker>();
        var clients = new ClientAddress(options.TrustedProxyRanges);
        builder.Services.AddRateLimiter(limits =>
        {
            limits.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limits.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(clients.Of(context), _ => Window(options.RequestsPerMinute, TimeSpan.FromMinutes(1))));
            limits.AddPolicy(ReportEndpoints.RateLimitPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(clients.Of(context), _ => Window(options.ReportsPerHour, TimeSpan.FromHours(1))));
        });

        var app = builder.Build();
        app.Lifetime.ApplicationStopped.Register(() =>
        {
            writer.Dispose();
            reader.Dispose();
            views.Dispose();
        });
        app.Use((context, next) =>
        {
            context.Response.Headers.AccessControlAllowOrigin = "*";
            return next(context);
        });
        app.UseRateLimiter();
        app.MapRelease();
        app.MapVotes();
        app.MapPartyAgreement();
        app.MapBoards();
        app.MapMemberVotes();
        app.MapBills();
        app.MapStatements();
        app.MapDocumentChanges();
        app.MapBriefs();
        app.MapZipDistricts();
        app.MapIssues();
        app.MapHome();
        app.MapRelated();
        app.MapWithdrawals();
        app.MapExecutiveActions();
        app.MapOfficials();
        app.MapSearch();
        app.MapReports(options);
        if (options.PublicBaseUrl is { } publicBaseUrl)
        {
            app.MapSharedAssets();
            app.MapServerPages(publicBaseUrl);
        }
        return app;
    }

    private static FixedWindowRateLimiterOptions Window(int permits, TimeSpan window) =>
        new() { PermitLimit = permits, Window = window, QueueLimit = 0 };
}
