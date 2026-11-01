using System.Net;
using CivicLens.Tests.Infrastructure.Collection;
using static CivicLens.Tests.Host.ReviewWorkspaceHost;

namespace CivicLens.Tests.Host;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class TakeOffSiteWorkspaceTests(PostgresCollection postgres) : IAsyncLifetime
{
    private readonly ReviewWorkspaceHost host = new(postgres)
    {
        WithReleaseDirectory = false
    };

    public Task InitializeAsync() => host.StartAsync();

    public Task DisposeAsync() => host.StopAsync();

    [Fact]
    public async Task WithoutTheReleaseDirectoryTheOwnerSeesOneLineAndNoButtonBecauseTheReviewHostCannotWithdrawAsync()
    {
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|owner"));

        var editor = await host.GetDocumentAsync(await StartDraftAsync());

        Assert.Equal("Take off site needs the release directory, collection and issue files", editor.QuerySelector("#take-off-site-missing")!.TextContent);
        Assert.Null(editor.QuerySelector("#take-off-site"));
        Assert.DoesNotContain(editor.QuerySelectorAll("button"), button => button.TextContent.Contains("Take off site", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WithoutTheReleaseDirectoryAReviewerSeesNoLineBecauseOnlyTheOwnerCanFixTheConfigurationAsync()
    {
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|friend"));

        var editor = await host.GetDocumentAsync(await StartDraftAsync());

        Assert.Null(editor.QuerySelector("#take-off-site-missing"));
        Assert.Null(editor.QuerySelector("#take-off-site"));
    }

    private async Task<string> StartDraftAsync()
    {
        var before = await host.SaveExtractionAsync("Fee: 10 dollars.\n");
        var after = await host.SaveExtractionAsync("Fee: 12 dollars.\n");
        var comparison = await host.Comparisons.CompareAndSaveAsync(before.ExtractionId, after.ExtractionId, default);
        var home = await host.GetDocumentAsync("/Review");
        var fields = Fields(home.QuerySelector("#start-draft")!);
        fields.Add(new("comparisonId", comparison.ComparisonId));
        using var created = await host.Client.PostAsync("/Review", new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var location = created.Headers.Location!;
        return location.IsAbsoluteUri ? location.PathAndQuery : location.ToString();
    }
}
