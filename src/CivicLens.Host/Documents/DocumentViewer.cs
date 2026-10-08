using System.Net;
using CivicLens.Application.Documents;
using CivicLens.Infrastructure.Documents;

namespace CivicLens.Host.Documents;

internal static class DocumentViewer
{
    public static async Task<int> RunAsync(int port, CancellationToken cancellationToken)
    {
        var connectionString = Environment.GetEnvironmentVariable("CIVIC_LENS_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine("CIVIC_LENS_DATABASE is required to run the document viewer.");
            return 2;
        }

        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                Args = [],
                ApplicationName = typeof(DocumentViewer).Assembly.GetName().Name,
                EnvironmentName = Environments.Production,
                ContentRootPath = AppContext.BaseDirectory,
                WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot")
            });
            // This local viewer has one explicit listener; ambient endpoint settings must not widen it.
            builder.Configuration.Sources.Clear();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, port));
            builder.Services.AddRazorPages();

            builder.Services.AddSingleton<IDocumentExtractionStore>(
                PostgresDocumentExtractionStore.FromConnectionString(connectionString));
            builder.Services.AddSingleton<IDocumentHistoryStore>(
                PostgresDocumentHistoryStore.FromConnectionString(connectionString));
            builder.Services.AddSingleton<IDocumentComparisonStore>(
                PostgresDocumentComparisonStore.FromConnectionString(connectionString));
            builder.Services.AddScoped<GetDocumentHistory>();
            builder.Services.AddScoped<GetDocumentComparison>();
            builder.Services.AddScoped<GetDocumentCitation>();

            await using var app = builder.Build();
            app.Use(async (context, next) =>
            {
                SetSecurityHeaders(context.Response);

                if (context.Request.Path.StartsWithSegments("/Review", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                if (!IsLoopbackHost(context.Request.Host.Host))
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
                {
                    context.Response.Headers["Allow"] = "GET, HEAD";
                    context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                    return;
                }

                try
                {
                    await next();
                }
                catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    if (!context.Response.HasStarted)
                    {
                        context.Response.Clear();
                        SetSecurityHeaders(context.Response);
                        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    }
                }
            });
            app.UseStaticFiles();
            app.MapRazorPages();

            using var registration = cancellationToken.Register(app.Lifetime.StopApplication);
            await app.StartAsync(cancellationToken);
            Console.WriteLine($"Document viewer listening at http://127.0.0.1:{port}/");
            await app.WaitForShutdownAsync(cancellationToken);
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return 0;
        }
        catch (ArgumentException)
        {
            Console.Error.WriteLine("Invalid CIVIC_LENS_DATABASE. Use a Postgres connection string with Host and Database.");
            return 2;
        }
        catch
        {
            Console.Error.WriteLine("Document viewer failed to start or stop cleanly. Check the database, port, and applied migrations.");
            return 1;
        }
    }

    private static bool IsLoopbackHost(string host) =>
        IPAddress.TryParse(host, out var address)
            ? IPAddress.IsLoopback(address)
            : string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase);

    private static void SetSecurityHeaders(HttpResponse response)
    {
        response.Headers["Cache-Control"] = "no-store";
        response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'self'; script-src 'self'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'";
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["X-Frame-Options"] = "DENY";
    }
}
