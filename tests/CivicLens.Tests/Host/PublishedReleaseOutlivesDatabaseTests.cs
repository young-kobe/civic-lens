using System.Text.Json;
using AngleSharp.Html.Parser;
using CivicLens.Application.Publication;
using CivicLens.Application.Review;
using CivicLens.Core.Review;
using CivicLens.Host.Publication;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Documents;
using CivicLens.Infrastructure.Publication;
using CivicLens.Infrastructure.Review;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using Testcontainers.PostgreSql;

namespace CivicLens.Tests.Host;

[Trait("Category", "Postgres")]
public sealed class PublishedReleaseOutlivesDatabaseTests : IAsyncLifetime
{
    private static readonly ReviewActor Owner = new("auth0|owner", ReviewRole.Owner);
    private readonly string root = Path.Combine(Path.GetTempPath(), "civic-outlive-" + Guid.NewGuid().ToString("N"));
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder(
        "postgres:18.3-alpine@sha256:54451ecb8ab38c24c3ec123f2fd501303a3a1856a5c66e98cecf2460d5e1e9d7")
        .WithDatabase("civic_lens")
        .WithUsername("civic_lens")
        .WithPassword("civic_lens")
        .Build();

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(root);
        await container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await container.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task PublishedReleaseServesFromFilesAloneAfterTheDatabaseIsGone()
    {
        var releases = Path.Combine(root, "releases-root");
        var summary = await PublishAsync(releases);
        await container.DisposeAsync();

        var current = Path.Combine(releases, "current");
        var manifest = Deserialize<PublicationRelease>(Path.Combine(current, PublicationProtocol.ManifestPath));
        manifest.Validate();
        var entry = Assert.Single(manifest.Records);
        var record = Deserialize<PublishedDocumentChange>(Path.Combine(current, PublicationProtocol.RecordDataPath(entry.RecordId)));
        record.Validate();
        Assert.Equal(summary.ReleaseNumber, manifest.ReleaseNumber);
        Assert.Equal("Mayor Example", Assert.Single(record.Officials).Name);

        var indexPage = Path.Combine(current, PublicationProtocol.IndexPagePath(1));
        var recordPage = Path.Combine(current, PublicationProtocol.RecordPagePath(entry.RecordId));
        AssertLocalLinksResolve(current, indexPage);
        AssertLocalLinksResolve(current, recordPage);
        AssertRecordPageCarriesTheEvidence(recordPage, record);
    }

    private async Task<PublicationReleaseSummary> PublishAsync(string releasesRoot)
    {
        var captureRoot = Path.Combine(root, "captures");
        Directory.CreateDirectory(captureRoot);
        var connectionString = new NpgsqlConnectionStringBuilder(container.GetConnectionString()) { Pooling = false }.ConnectionString;
        var factory = new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
        var attempts = new PostgresCollectionAttemptStore(factory);
        var extractions = new PostgresDocumentExtractionStore(factory);
        var reviews = new PostgresDocumentChangeReviewStore(factory);
        await attempts.MigrateAsync();
        var comparison = await new ComparisonSeeder(attempts, extractions, new PostgresDocumentComparisonStore(factory), captureRoot)
            .SaveAsync("The permit fee is 50 dollars.", "The permit fee is 75 dollars.");

        var catalog = new ReviewCatalog(["housing"], ["mayor"]);
        var draft = await new CreateDocumentChangeDraft(reviews).ExecuteAsync(Owner, new(comparison.ComparisonId, "create"));
        var saved = await new SaveDocumentChangeDraft(reviews, catalog).ExecuteAsync(Owner,
            new(draft.DraftId, 1, "Permit fee rises", "The fee increased.", null, "Only this page was compared.",
                "City Council", null, null, ["mayor"], ["housing"], draft.Citations, "save"));
        await new DecideDocumentChangeReview(reviews, catalog).ExecuteAsync(Owner,
            new(saved.DraftId, 2, 0, ReviewDecisionKind.Approve, null, [], "approve"));

        var publish = new PublishDocumentChanges(PostgresPublicationStore.FromConnectionString(connectionString),
            new FileReleaseDirectory(releasesRoot), new ReleaseRenderer(), reviews,
            new PublicationCatalog(new Dictionary<string, string> { ["mayor"] = "Mayor Example" }), TimeProvider.System);
        return await publish.ExecuteAsync(Owner, new PublishDocumentChangesRequest([saved.DraftId], "publish"));
    }

    private static T Deserialize<T>(string path) =>
        JsonSerializer.Deserialize<T>(File.ReadAllBytes(path), PublicationProtocol.JsonOptions)!;

    private static void AssertLocalLinksResolve(string current, string pagePath)
    {
        var document = new HtmlParser().ParseDocument(File.ReadAllText(pagePath));
        var references = document.QuerySelectorAll("[href], [src]")
            .Select(element => element.GetAttribute("href") ?? element.GetAttribute("src")!)
            .Where(reference => !reference.StartsWith("#", StringComparison.Ordinal) &&
                !reference.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !reference.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.NotEmpty(references);
        foreach (var reference in references)
        {
            var relative = reference.Split('#', '?')[0];
            var target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(pagePath)!, relative));
            Assert.StartsWith(current + Path.DirectorySeparatorChar, target, StringComparison.Ordinal);
            Assert.True(File.Exists(target), $"{reference} in {Path.GetFileName(pagePath)} does not resolve inside the release.");
        }
    }

    private static void AssertRecordPageCarriesTheEvidence(string recordPage, PublishedDocumentChange record)
    {
        var document = new HtmlParser().ParseDocument(File.ReadAllText(recordPage));
        var quotes = document.QuerySelectorAll("blockquote").Select(quote => quote.TextContent).ToList();
        var versions = document.QuerySelectorAll("pre.public-text").Select(text => text.TextContent).ToList();

        Assert.All(record.Citations, citation => Assert.Contains(citation.Quote, quotes));
        Assert.Equal([record.Before.Text, record.After.Text], versions);
    }
}
