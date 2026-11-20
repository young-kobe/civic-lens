using CivicLens.PublicWeb.Publication;

namespace CivicLens.Tests.PublicWeb;

public sealed class AboutPageTests
{
    [Theory]
    [InlineData("Our records, summaries and weekly briefs", "Free to reuse, also for business, under CC BY 4.0.", "https://creativecommons.org/licenses/by/4.0/")]
    [InlineData("Source code", "AGPL-3.0, on GitHub.", "https://github.com/young-kobe/civic-lens")]
    public async Task ReuseTermsKeepAWordBoundaryBeforeTheLicenseLink(string material, string expectedText, string licenseUrl)
    {
        var page = await ListPageRender.RenderPageAsync<AboutPage>(new Dictionary<string, object?>
        {
            [nameof(AboutPage.RootPath)] = ListPageRender.Root
        });

        var row = Assert.Single(page.QuerySelectorAll("tbody tr"), row => row.QuerySelector(".cell-main")?.TextContent == material);
        var terms = row.QuerySelector("td[data-label='Terms']")!;

        Assert.Equal(expectedText, terms.TextContent);
        Assert.Equal(licenseUrl, Assert.Single(terms.QuerySelectorAll("a")).GetAttribute("href"));
    }
}
