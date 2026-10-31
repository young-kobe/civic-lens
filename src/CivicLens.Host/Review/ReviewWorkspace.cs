using System.Net;
using CivicLens.Application.Actions;
using CivicLens.Application.Activity;
using CivicLens.Application.Analysis;
using CivicLens.Application.Briefs;
using CivicLens.Application.Collection;
using CivicLens.Application.Documents;
using CivicLens.Application.ExecutiveActions;
using CivicLens.Application.Publication;
using CivicLens.Application.Relationships;
using CivicLens.Application.Reports;
using CivicLens.Application.Issues;
using CivicLens.Application.Review;
using CivicLens.Application.Statements;
using CivicLens.Application.Votes;
using CivicLens.Infrastructure.Actions;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Briefs;
using CivicLens.Infrastructure.Documents;
using CivicLens.Infrastructure.ExecutiveActions;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Jobs;
using CivicLens.Infrastructure.Publication;
using CivicLens.Infrastructure.Relationships;
using CivicLens.Infrastructure.Reports;
using CivicLens.Infrastructure.Roster;
using CivicLens.Infrastructure.Review;
using CivicLens.Infrastructure.Statements;
using CivicLens.Infrastructure.Votes;
using CivicLens.Host.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.Host.Components;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace CivicLens.Host.Review;

internal static class ReviewWorkspace
{
    public static async Task<int> RunAsync(int port, CancellationToken cancellationToken)
    {
        ReviewWorkspaceSettings settings;
        string connectionString;
        try
        {
            settings = ReviewWorkspaceSettings.FromEnvironment();
            connectionString = Environment.GetEnvironmentVariable("CIVIC_LENS_DATABASE")
                ?? throw new ArgumentException("CIVIC_LENS_DATABASE is required.");
            _ = PostgresDocumentComparisonStore.FromConnectionString(connectionString);
            if (Environment.GetEnvironmentVariable("CIVIC_LENS_RELEASE_DIRECTORY") is not null)
            {
                PublicationComposition.RequireArtifactRoot();
                _ = ReleaseRenderer.PublicBaseUrlFromEnvironment();
            }
        }
        catch (ArgumentException)
        {
            Console.Error.WriteLine("Invalid review workspace configuration. Configure the database, HTTPS origin, Auth0 credentials, authorized subjects, persistent key directory, the artifact directory when the release directory is set, and a valid public base URL when it is set. See docs/operations.md.");
            return 2;
        }

        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                Args = [],
                ApplicationName = typeof(ReviewWorkspace).Assembly.GetName().Name,
                EnvironmentName = Environments.Production,
                ContentRootPath = AppContext.BaseDirectory,
                WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot")
            });
            builder.Configuration.Sources.Clear();
            // Build output lacks framework scripts such as blazor.web.js; publish output includes them.
            builder.WebHost.UseStaticWebAssets();
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole(console => console.LogToStandardErrorThreshold = LogLevel.Trace);
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Listen(IPAddress.Loopback, port);
                options.Limits.MaxRequestBodySize = 128 * 1024;
            });
            builder.Services.AddSingleton(settings);
            builder.Services.AddSingleton<ReviewActorAccessor>();
            var directory = Directory.CreateDirectory(settings.KeyDirectory);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(directory.FullName, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            builder.Services.AddDataProtection().SetApplicationName("CivicLens.Review")
                .PersistKeysToFileSystem(directory);
            ConfigureAuthentication(builder.Services, settings);
            builder.Services.AddAuthorization(options =>
            {
                var policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser()
                    .RequireAssertion(context => new ReviewActorAccessor(settings).IsAuthorized(context.User)).Build();
                options.DefaultPolicy = policy;
                options.FallbackPolicy = policy;
            });
            builder.Services.AddRazorComponents().AddInteractiveServerComponents();
            builder.Services.AddCascadingAuthenticationState();
            ConfigureReviewServices(builder.Services, connectionString);

            await using var app = builder.Build();
            app.Use(async (context, next) =>
            {
                SetHeaders(context.Response);
                if (!string.Equals(context.Request.Host.Value, settings.Origin.Authority, StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }
                // Only the loopback reverse proxy reaches this listener. Never trust forwarded headers.
                context.Request.Scheme = "https";
                try { await next(); }
                catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { throw; }
                catch (Exception exception)
                {
                    app.Logger.LogError(exception, "A review request failed.");
                    if (!context.Response.HasStarted)
                    {
                        context.Response.Clear();
                        SetHeaders(context.Response);
                        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                        await context.Response.WriteAsync("The review workspace is temporarily unavailable.");
                    }
                }
            });
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseAntiforgery();
            app.MapStaticAssets().AllowAnonymous();
            app.MapGet("/login", () => Results.Challenge(new AuthenticationProperties { RedirectUri = "/Review" },
                [OpenIdConnectDefaults.AuthenticationScheme])).AllowAnonymous();
            app.MapGet("/access-denied", () => Results.Text("This account does not have editorial access.", statusCode: 403)).AllowAnonymous();
            app.Use(async (context, next) =>
            {
                if (context.Request.Path == "/") context.Response.Redirect("/Review");
                else await next();
            });
            app.MapGet("/Review/Logout", () => Results.StatusCode(StatusCodes.Status405MethodNotAllowed));
            app.MapPost("/Review/Logout", (Delegate)SignOutAsync).WithMetadata(new RequireAntiforgeryTokenAttribute());
            app.MapBlazorHub(options => options.CloseOnAuthenticationExpiration = true).WithOrder(-1);
            app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
            using var registration = cancellationToken.Register(app.Lifetime.StopApplication);
            await app.StartAsync(cancellationToken);
            Console.WriteLine($"Review workspace listening on loopback port {port}; HTTPS origin {settings.Origin.GetLeftPart(UriPartial.Authority)}.");
            await app.WaitForShutdownAsync(cancellationToken);
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return 0; }
        catch
        {
            Console.Error.WriteLine("Review workspace failed. Check configuration, persistent storage, and applied migrations.");
            return 1;
        }
    }

    private static async Task<IResult> SignOutAsync(HttpContext context)
    {
        if (context.Features.Get<IAntiforgeryValidationFeature>()?.IsValid != true) return Results.BadRequest();
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.Text("You have signed out of Civic Lens. Your identity-provider session may still be active.");
    }

    private static void ConfigureAuthentication(IServiceCollection services, ReviewWorkspaceSettings settings)
    {
        services.AddAuthentication(options =>
        {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
        }).AddCookie(options =>
        {
            options.Cookie.Name = "__Host-CivicLens.Review";
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = false;
            options.AccessDeniedPath = "/access-denied";
            options.Events.OnValidatePrincipal = context =>
            {
                if (!new ReviewActorAccessor(settings).IsAuthorized(context.Principal!)) context.RejectPrincipal();
                return Task.CompletedTask;
            };
        }).AddOpenIdConnect(options =>
        {
            options.Authority = settings.Authority.AbsoluteUri;
            options.ClientId = settings.ClientId;
            options.ClientSecret = settings.ClientSecret;
            options.ResponseType = OpenIdConnectResponseType.Code;
            options.UsePkce = true;
            options.MapInboundClaims = false;
            options.SaveTokens = false;
            options.Scope.Clear();
            options.Scope.Add("openid");
            options.CallbackPath = "/signin-oidc";
            options.PushedAuthorizationBehavior = PushedAuthorizationBehavior.Disable;
            options.Events.OnTokenValidated = context =>
            {
                if (!new ReviewActorAccessor(settings).IsAuthorized(context.Principal!))
                    context.Fail("Editorial access is not granted.");
                return Task.CompletedTask;
            };
            options.Events.OnRemoteFailure = context =>
            {
                context.HandleResponse();
                context.Response.Redirect("/access-denied");
                return Task.CompletedTask;
            };
        });
    }

    private static void ConfigureReviewServices(IServiceCollection services, string connectionString)
    {
        services.AddSingleton<IDocumentExtractionStore>(PostgresDocumentExtractionStore.FromConnectionString(connectionString));
        services.AddSingleton<IDocumentHistoryStore>(PostgresDocumentHistoryStore.FromConnectionString(connectionString));
        services.AddSingleton<IDocumentComparisonStore>(PostgresDocumentComparisonStore.FromConnectionString(connectionString));
        services.AddScoped<GetDocumentHistory>();
        services.AddScoped<GetDocumentComparison>();
        services.AddScoped<GetDocumentCitation>();
        var configuredIssues = ConfigurationFile.ReadIssuesFromEnvironment();
        var issues = configuredIssues ?? IssueConfiguration.None;
        services.AddSingleton<IDocumentChangeReviewStore>(PostgresDocumentChangeReviewStore.FromConnectionString(connectionString));
        services.AddSingleton<IDocumentChangeAnalysisStore>(PostgresDocumentChangeAnalysisStore.FromConnectionString(connectionString));
        services.AddScoped<ListDocumentChangeAnalyses>();
        services.AddScoped<GetDocumentChangeAnalysisRun>();
        services.AddSingleton<IAnalysisStatusStore>(PostgresAnalysisStatusStore.FromConnectionString(connectionString));
        services.AddScoped<GetDocumentChangeAnalysisStatus>();
        services.AddScoped<CreateDocumentChangeDraft>();
        services.AddScoped<SaveDocumentChangeDraft>();
        services.AddScoped<DecideDocumentChangeReview>();
        services.AddScoped<GetDocumentChangeReview>();
        services.AddScoped<ListEligibleDocumentComparisons>();
        var contexts = AnalysisDbContextFactory.Create(connectionString);
        ConfigureStatementReview(services, contexts, issues);
        ConfigureActionReview(services, contexts);
        ConfigureVoteReview(services, contexts);
        ConfigureExecutiveActionReview(services, contexts);
        ConfigureRelationshipReview(services, contexts);
        ConfigureBriefReview(services, contexts);
        ConfigureReportReview(services, contexts);
        services.AddSingleton<IRecordKindStore>(new PostgresRecordKindStore(contexts));
        services.AddSingleton<IReviewQueueStore>(new PostgresReviewQueueStore(contexts));
        services.AddScoped<ListReviewQueue>();
        services.AddScoped<SearchApprovedRecords>();
        services.AddScoped(provider => new LookupDraft(provider.GetRequiredService<IActionReviewStore>(),
            provider.GetRequiredService<IVoteReviewStore>(), provider.GetRequiredService<IRecordKindStore>(), TimeProvider.System));
        var collection = ConfigurationFile.ReadCollectionFromEnvironment();
        var officials = ReviewOfficials.From(collection);
        services.AddSingleton(officials);
        services.AddSingleton(new ReviewCatalog(issues.Issues.Select(issue => issue.Id), officials.People.Select(person => person.Key)));
        if (collection is null)
        {
            services.AddScoped(provider => new GetRecentActivity(provider.GetRequiredService<IDocumentChangeReviewStore>()));
        }
        else
        {
            services.AddScoped(provider => new GetRecentActivity(provider.GetRequiredService<IDocumentChangeReviewStore>(),
                PostgresCollectionJobStore.FromConnectionString(connectionString),
                CivicLens.Infrastructure.Collection.Processing.PostgresEvidenceProcessingStore.FromConnectionString(connectionString)));
            services.AddScoped(_ => new CollectionWorkspace(collection,
                PostgresCollectionJobStore.FromConnectionString(connectionString),
                PostgresCollectionAttemptStore.FromConnectionString(connectionString),
                CivicLens.Infrastructure.Collection.Processing.PostgresEvidenceProcessingStore.FromConnectionString(connectionString)));
            if (configuredIssues is not null) ConfigurePublication(services, connectionString);
        }
    }

    private static void ConfigureStatementReview(IServiceCollection services, IDbContextFactory<CollectionAttemptDbContext> contexts,
        IssueConfiguration issues)
    {
        services.AddSingleton<IStatementReviewStore>(new PostgresStatementReviewStore(contexts));
        services.AddSingleton<IStatementDraftStore>(new PostgresStatementDraftStore(contexts));
        services.AddSingleton(issues);
        services.AddSingleton<StatementSelectionResolver>();
        services.AddScoped<StatementReviewContextReader>();
        services.AddScoped<CreateStatementDraft>();
        services.AddScoped<SaveStatementDraft>();
        services.AddScoped<DecideStatementReview>();
        services.AddScoped<ListStatementTriage>();
        services.AddScoped<GetStatementReview>();
        services.AddScoped<ListStatementReviews>();
        services.AddScoped<GetStatementReviewCounts>();
        services.AddScoped<ListStatementsNotInReview>();
        services.AddSingleton<IGateLabelStore>(new PostgresGateLabelStore(contexts));
        services.AddScoped<SaveGateLabel>();
        services.AddScoped<ListGateLabelQueue>();
        services.AddScoped<GetGateLabelTask>();
    }

    private static void ConfigureActionReview(IServiceCollection services, IDbContextFactory<CollectionAttemptDbContext> contexts)
    {
        services.AddSingleton<IActionReviewStore>(new PostgresActionReviewStore(contexts));
        services.AddScoped<SaveActionDraft>();
        services.AddScoped<DecideActionReview>();
        services.AddScoped<GetActionReview>();
    }

    private static void ConfigureVoteReview(IServiceCollection services, IDbContextFactory<CollectionAttemptDbContext> contexts)
    {
        services.AddSingleton<IVoteReviewStore>(new PostgresVoteReviewStore(contexts));
        services.AddScoped<SaveVoteDraft>();
        services.AddScoped<DecideVoteReview>();
        services.AddScoped<GetVoteReview>();
    }

    private static void ConfigureExecutiveActionReview(IServiceCollection services, IDbContextFactory<CollectionAttemptDbContext> contexts)
    {
        services.AddSingleton<IExecutiveActionReviewStore>(new PostgresExecutiveActionReviewStore(contexts));
        services.AddScoped<DecideExecutiveActionReview>();
        services.AddScoped<GetExecutiveActionReview>();
    }

    private static void ConfigureRelationshipReview(IServiceCollection services, IDbContextFactory<CollectionAttemptDbContext> contexts)
    {
        services.AddSingleton<IRelationshipReviewStore>(new PostgresRelationshipReviewStore(contexts));
        services.AddScoped<SaveRelationshipDraft>();
        services.AddScoped<DecideRelationshipReview>();
        services.AddScoped<GetRelationshipReview>();
        services.AddScoped<ListRelationshipReviews>();
        services.AddScoped<GetRelationshipReviewCounts>();
    }

    private static void ConfigureReportReview(IServiceCollection services, IDbContextFactory<CollectionAttemptDbContext> contexts)
    {
        services.AddSingleton<IReaderReportStore>(new PostgresReaderReportStore(contexts));
        services.AddScoped<ResolveReaderReport>();
        services.AddScoped<GetReaderReport>();
        services.AddScoped<ListReaderReports>();
        services.AddScoped<CountOpenReaderReports>();
        services.AddScoped<ListUnansweredCorrections>();
    }

    private static void ConfigureBriefReview(IServiceCollection services, IDbContextFactory<CollectionAttemptDbContext> contexts)
    {
        services.AddSingleton<IBriefReviewStore>(new PostgresBriefReviewStore(contexts));
        services.AddScoped<SaveBriefDraft>();
        services.AddScoped<DecideBriefReview>();
        services.AddScoped<GetBriefReview>();
        services.AddScoped<ListBriefReviews>();
        services.AddScoped<GetBriefReviewCounts>();
    }

    // The owner release lists need a release directory.
    private static void ConfigurePublication(IServiceCollection services, string connectionString)
    {
        var releaseRoot = Environment.GetEnvironmentVariable("CIVIC_LENS_RELEASE_DIRECTORY");
        if (releaseRoot is null) return;
        var publications = PostgresPublicationStore.FromConnectionString(connectionString);
        var releases = new FileReleaseDirectory(releaseRoot);
        services.AddScoped(_ => new ListPublicationReleases(publications, releases));
        services.AddScoped(_ => new ListBlockedPublished(publications, releases, new BlockedPublishedRecords(publications, releases)));
    }

    private static void SetHeaders(HttpResponse response)
    {
        response.Headers.CacheControl = "no-store";
        response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'self'; script-src 'self'; connect-src 'self'; img-src 'self'; font-src 'self'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers.XContentTypeOptions = "nosniff";
        response.Headers.XFrameOptions = "DENY";
    }
}
