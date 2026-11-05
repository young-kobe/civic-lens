using System.Net;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Spending;
using CivicLens.Tests.Infrastructure.Collection;
using static CivicLens.Tests.Host.ReviewWorkspaceHost;

namespace CivicLens.Tests.Host;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class AiSpendingPageTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string Owner = "auth0|owner";
    private const string Reviewer = "auth0|friend";
    private const string Page = "/Review/Spending";
    private const string Haiku = "claude-haiku-5-5";
    private readonly ReviewWorkspaceHost host = new(postgres);
    private PostgresAiSpendingStore spending = null!;

    public async Task InitializeAsync()
    {
        await host.StartAsync();
        spending = new PostgresAiSpendingStore(AnalysisDbContextFactory.Create(host.ConnectionString));
    }

    public Task DisposeAsync() => host.StopAsync();

    [Fact]
    public async Task ReviewersAreSentToAccessDeniedSoOnlyTheOwnerSetsTheCap()
    {
        SignIn(Reviewer);

        using var denied = await host.Client.GetAsync(Page);

        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Contains("access-denied", denied.Headers.Location!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheOwnerSeesTheCapTheMonthSpendAndThePriceOfEachModelThatAStageUses()
    {
        SignIn(Owner);

        var page = await host.GetDocumentAsync(Page);

        var text = page.QuerySelector("[aria-labelledby='month']")!.TextContent;
        Assert.Contains("$0.00 spent or reserved", text, StringComparison.Ordinal);
        Assert.Contains("$1000000.00", text, StringComparison.Ordinal);
        var prices = page.QuerySelectorAll("[aria-labelledby='prices'] tbody tr").Select(row => row.TextContent).ToArray();
        Assert.Equal(2, prices.Length);
        Assert.Contains(prices, price => price.Contains("claude-sonnet-5-5", StringComparison.Ordinal) && price.Contains("$15.00", StringComparison.Ordinal));
        Assert.Empty(page.QuerySelectorAll(".notice"));
    }

    [Fact]
    public async Task ASavedCapIsStoredInMicroDollarsAndShownInTheLastChanges()
    {
        SignIn(Owner);
        var page = await host.GetDocumentAsync(Page);
        var fields = Fields(page.QuerySelector("#cap-form")!);
        Set(fields, "Cap.Dollars", "12.5");

        using var saved = await host.Client.PostAsync(Page, new FormUrlEncodedContent(fields));

        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        var cap = (await spending.GetAsync(10, default)).CurrentCap!;
        Assert.Equal((12_500_000, Owner, 2), (cap.CapMicros, cap.AuthorSubject, cap.RevisionNumber));
        var after = await host.GetDocumentAsync(Page);
        Assert.Contains("Monthly cap set to $12.50", after.QuerySelector("[aria-labelledby='changes']")!.TextContent, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("0.0000001")]
    [InlineData("1000001")]
    public async Task ACapThatIsNotAWholeMicroDollarAmountInRangeIsRefusedAndWritesNothing(string dollars)
    {
        SignIn(Owner);
        var fields = Fields((await host.GetDocumentAsync(Page)).QuerySelector("#cap-form")!);
        Set(fields, "Cap.Dollars", dollars);

        using var refused = await host.Client.PostAsync(Page, new FormUrlEncodedContent(fields));

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(1, (await spending.GetAsync(10, default)).CurrentCap!.RevisionNumber);
    }

    [Fact]
    public async Task ASavedPriceAddsARevisionForItsModelOnly()
    {
        SignIn(Owner);

        using var saved = await host.Client.PostAsync(Page, await PriceFormAsync(Haiku, "2", "10", "0.2", "2.5"));

        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        var view = await spending.GetAsync(10, default);
        var haiku = view.CurrentPrices.Single(price => price.Price.Model == Haiku);
        Assert.Equal((2, 2_000_000, 10_000_000, 200_000, 2_500_000),
            (haiku.RevisionNumber, haiku.Price.InputMicros, haiku.Price.OutputMicros, haiku.Price.CacheReadMicros, haiku.Price.CacheWriteMicros));
        Assert.Equal(1, view.CurrentPrices.Single(price => price.Price.Model == "claude-sonnet-5-5").RevisionNumber);
    }

    [Theory]
    [InlineData("claude-not-used", "1")]
    [InlineData(Haiku, "1001")]
    [InlineData(Haiku, "x")]
    public async Task APriceForAModelNoStageUsesOrOutsideTheRangeIsRefusedAndWritesNothing(string model, string input)
    {
        SignIn(Owner);

        using var refused = await host.Client.PostAsync(Page, await PriceFormAsync(model, input, "1", "1", "1"));

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.All((await spending.GetAsync(10, default)).CurrentPrices, price => Assert.Equal(1, price.RevisionNumber));
    }

    private async Task<FormUrlEncodedContent> PriceFormAsync(string model, string input, string output, string cacheRead, string cacheWrite)
    {
        var fields = Fields((await host.GetDocumentAsync(Page)).QuerySelector("#price-form")!);
        Set(fields, "Price.Model", model);
        Set(fields, "Price.Input", input);
        Set(fields, "Price.Output", output);
        Set(fields, "Price.CacheRead", cacheRead);
        Set(fields, "Price.CacheWrite", cacheWrite);
        return new(fields);
    }

    private void SignIn(string subject) => host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie(subject));
}
