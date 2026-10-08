using System.Net;
using CivicLens.Application.Collection;
using CivicLens.Application.Documents;
using CivicLens.Application.Review;
using CivicLens.Infrastructure.Documents;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Jobs;
using CivicLens.Infrastructure.Review;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
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
        }
        catch (ArgumentException)
        {
            Console.Error.WriteLine("Invalid review workspace configuration. Configure the database, HTTPS origin, Auth0 credentials, authorized subjects, and persistent key directory. See docs/operations.md.");
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
            builder.Logging.ClearProviders();
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
            builder.Services.AddRazorPages(options => options.Conventions.AddPageRoute("/Index", "/Evidence"));
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
                catch
                {
                    if (!context.Response.HasStarted)
                    {
                        context.Response.Clear();
                        SetHeaders(context.Response);
                        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                        await context.Response.WriteAsync("The review workspace is temporarily unavailable.");
                    }
                }
            });
            app.UseStaticFiles();
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapGet("/login", () => Results.Challenge(new AuthenticationProperties { RedirectUri = "/Review" },
                [OpenIdConnectDefaults.AuthenticationScheme])).AllowAnonymous();
            app.MapGet("/access-denied", () => Results.Text("This account does not have editorial access.", statusCode: 403)).AllowAnonymous();
            app.Use(async (context, next) =>
            {
                if (context.Request.Path == "/") context.Response.Redirect("/Review");
                else await next();
            });
            app.MapRazorPages();
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
        services.AddSingleton(new ReviewCatalog(ReadIds("CIVIC_LENS_REVIEW_ISSUES"), ReadIds("CIVIC_LENS_REVIEW_OFFICIALS")));
        services.AddSingleton<IDocumentChangeReviewStore>(PostgresDocumentChangeReviewStore.FromConnectionString(connectionString));
        services.AddScoped<CreateDocumentChangeDraft>();
        services.AddScoped<SaveDocumentChangeDraft>();
        services.AddScoped<DecideDocumentChangeReview>();
        services.AddScoped<GetDocumentChangeReview>();
        services.AddScoped<ListDocumentChangeReviews>();
        services.AddScoped<ListEligibleDocumentComparisons>();
        var collection = ReviewCollectionSettings.FromEnvironment();
        if (collection is not null)
        {
            services.AddScoped(_ =>
            {
                var jobs = PostgresCollectionJobStore.FromConnectionString(connectionString);
                var attempts = PostgresCollectionAttemptStore.FromConnectionString(connectionString);
                var extractions = PostgresDocumentExtractionStore.FromConnectionString(connectionString);
                var comparisons = PostgresDocumentComparisonStore.FromConnectionString(connectionString);
                var history = PostgresDocumentHistoryStore.FromConnectionString(connectionString);
                return new CollectionWorkspace(collection.Configuration, collection.ArtifactRoot, jobs, jobs,
                    new ExtractDocument(attempts, new CaptureDocumentTextExtractor(), extractions), extractions,
                    new CompareDocuments(extractions, comparisons), new GetDocumentHistory(history), attempts,
                    CivicLens.Infrastructure.Collection.Processing.PostgresEvidenceProcessingStore.FromConnectionString(connectionString));
            });
        }
    }

    private static string[] ReadIds(string name) => (Environment.GetEnvironmentVariable(name) ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static void SetHeaders(HttpResponse response)
    {
        response.Headers.CacheControl = "no-store";
        response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'self'; script-src 'self'; connect-src 'self'; img-src 'self'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'";
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers.XContentTypeOptions = "nosniff";
        response.Headers.XFrameOptions = "DENY";
    }
}
