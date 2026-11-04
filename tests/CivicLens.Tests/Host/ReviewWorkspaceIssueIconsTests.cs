using CivicLens.Tests.Infrastructure.Collection;

namespace CivicLens.Tests.Host;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class ReviewWorkspaceIssueIconsTests(PostgresCollection postgres)
{
    [Fact]
    public async Task AReviewHostWithTheIssuesFileButNoIconsFileRefusesToStartSoTakeOffSiteCannotPublishNeutralTilesAsync()
    {
        var host = new ReviewWorkspaceHost(postgres) { WithIssueIcons = false };

        try
        {
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(host.StartAsync);

            Assert.Contains("Review workspace exited", failure.Message, StringComparison.Ordinal);
        }
        finally
        {
            await host.StopAsync();
        }
    }
}
