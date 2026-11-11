using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using CivicLens.Application.Actions;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CivicLens.Tests.PublicWeb;

public sealed class PublicPageReadingGradeTests
{
    private const string Root = "https://civic-lens.test/";
    private const string DifficultSummary = "Notwithstanding the aforementioned administrative requirements, eligible beneficiaries receive compensatory disbursements.";

    [Theory]
    [InlineData("home")]
    [InlineData("compare")]
    public async Task HomeAndCompareIntroPassagesReadAtOrBelowTheSharedGradeLimit(string pageName)
    {
        var page = pageName == "home"
            ? await RenderAsync<HomePage>(new Dictionary<string, object?>
            {
                [nameof(HomePage.Data)] = new HomePageData(7, null, [], null),
                [nameof(HomePage.RootPath)] = Root
            })
            : await RenderAsync<ComparePage>(new Dictionary<string, object?>
            {
                [nameof(ComparePage.RootPath)] = Root
            });

        var intro = Assert.Single(page.QuerySelectorAll(".home-intro"));
        var heading = Assert.Single(intro.QuerySelectorAll("h1"));
        var lead = Assert.Single(intro.QuerySelectorAll(".home-lead"));
        var grade = PassageGrade(heading, lead);

        Assert.True(grade <= ReadingGrade.MaximumGrade,
            $"The {pageName} intro reads at grade {ReadingGrade.Format(grade)}.");
    }

    [Fact]
    public async Task TheBillPageFixedProseReadsAtOrBelowTheSharedGradeLimit()
    {
        var sponsor = new BillSponsorData("p1", "Pat One", "Democrat", new(2025, 9, 11));
        var data = new BillPageData(7, new string('a', 64), 1, 119, "H.R. 7", "Title", "Headline", "House", "Summary.", PublishedTextOrigin.Code, null, new(2025, 9, 11))
        {
            Sponsors = [sponsor],
            Cosponsors = new("House", [sponsor], [sponsor]),
            Status = "Passed the House. Now in the Senate.",
            StatusLabel = "Passed the House",
            CongressGovUrl = "https://www.congress.gov/bill/119th-congress/house-bill/7",
            Votes = [new(new string('b', 64), new(2026, 9, 16), 119, 2, 410, "Vote headline", null, "Vote to pass or agree to", "Passed", [])],
            StatusReadAtUtc = new(2026, 11, 5, 9, 57, 0, TimeSpan.Zero)
        };
        var page = await RenderAsync<BillDetailPage>(new Dictionary<string, object?>
        {
            [nameof(BillDetailPage.Data)] = data,
            [nameof(BillDetailPage.RootPath)] = Root
        });

        var grade = PassageGrade(page.QuerySelector(".bill-lede")!, page.QuerySelector(".bill-legend")!);
        var backers = PassageGrade(page.QuerySelector("#bill-backers-title")!, page.QuerySelector("#bill-backers-title")!.ParentElement!.QuerySelector(".bill-lede")!);

        var split = PassageGrade(page.QuerySelector("#bill-backers-title")!.ParentElement!.QuerySelector(".bill-lede")!, page.QuerySelector(".party-bar figcaption")!);
        var votes = PassageGrade(page.QuerySelector("#bill-votes-title")!, page.QuerySelector("#bill-votes-title")!.ParentElement!.QuerySelector(".bill-lede")!);
        var worst = new[] { grade, backers, split, votes }.Max();

        Assert.True(worst <= ReadingGrade.MaximumGrade, $"The bill page prose reads at grade {ReadingGrade.Format(worst)}.");
    }

    [Fact]
    public async Task FeaturedModelHeadlineAndSummaryReadAsOnePassageAtOrBelowTheSharedGradeLimit()
    {
        // Civic Lens model text captured on 2026-11-10, release 1050.
        // https://civic-lens.info/records/d5bb27866c52e5e9e1b4f8b5e3801dc6ca8b129d7e584746f92dad470e25b337.html
        var featured = new HomeFeaturedVoteData("d5bb27866c52e5e9e1b4f8b5e3801dc6ca8b129d7e584746f92dad470e25b337", new(2026, 9, 28),
            PublishedChamber.Senate,
            "Senate passes the Protect College Sports Act of 2026, a bill on student athlete name, image, and likeness rights and fair competition in college athletics",
            null,
            "The Senate voted on passage of the Protect College Sports Act of 2026. The question was whether the bill should pass, and the Senate answered yes, so the bill passed. The bill's stated purpose is to protect the name, image, and likeness rights of student athletes, to provide protections for them, and to promote fair competition among intercollegiate athletics, along with other purposes. The bill falls under the policy area of sports and recreation.",
            PublishedTextOrigin.Model, "Passed", 77, 22, null, null);
        var page = await RenderAsync<HomePage>(new Dictionary<string, object?>
        {
            [nameof(HomePage.Data)] = new HomePageData(1050, featured, [], null),
            [nameof(HomePage.RootPath)] = Root
        });

        var grade = FeaturedModelGrade(page);
        Assert.True(grade <= ReadingGrade.MaximumGrade,
            $"The featured model passage reads at grade {ReadingGrade.Format(grade)}.");
    }

    [Fact]
    public async Task EasyCopyOutsideTheFeaturedModelPassageCannotLowerItsGrade()
    {
        var featured = new HomeFeaturedVoteData(new string('a', 64), new(2026, 3, 3),
            PublishedChamber.House, "A clear headline", null, DifficultSummary,
            PublishedTextOrigin.Model, "Passed", 216, 214, null, null);
        var page = await RenderAsync<HomePage>(new Dictionary<string, object?>
        {
            [nameof(HomePage.Data)] = new HomePageData(7, featured, [], null),
            [nameof(HomePage.RootPath)] = Root
        });
        var isolatedGrade = FeaturedModelGrade(page);
        var modelSection = Assert.Single(page.QuerySelectorAll(".home-featured"));
        var easyCopy = page.CreateElement("p");
        easyCopy.TextContent = string.Join(' ', Enumerable.Repeat("The cat sat. The dog ran.", 12));
        modelSection.AppendChild(easyCopy);
        var gradeAfterUnrelatedCopy = FeaturedModelGrade(page);

        Assert.True(isolatedGrade > ReadingGrade.MaximumGrade);
        Assert.Equal(isolatedGrade, gradeAfterUnrelatedCopy);
        Assert.True(ReadingGrade.Rounded(NormalizeText(modelSection)) <= ReadingGrade.MaximumGrade,
            "The whole section can hide a difficult model passage behind unrelated easy text.");
    }

    private static double FeaturedModelGrade(IDocument page)
    {
        var modelSection = Assert.Single(page.QuerySelectorAll(".home-featured"));
        var headline = Assert.Single(modelSection.QuerySelectorAll("h2"));
        var summaryContainer = Assert.Single(modelSection.QuerySelectorAll(".home-featured-summary.home-ai-summary"));
        var summary = Assert.Single(summaryContainer.QuerySelectorAll("p"));
        return PassageGrade(headline, summary);
    }

    private static double PassageGrade(IElement heading, IElement body)
    {
        var pieces = new[] { NormalizeText(heading), NormalizeText(body) };
        Assert.All(pieces, piece => Assert.NotEmpty(piece));
        return ReadingGrade.RoundedPassage(pieces);
    }

    private static string NormalizeText(IElement element) =>
        string.Join(' ', element.TextContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static async Task<IDocument> RenderAsync<TComponent>(Dictionary<string, object?> parameters)
        where TComponent : IComponent
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<TComponent>(ParameterView.FromDictionary(parameters));
            return output.ToHtmlString();
        });
        return await new HtmlParser().ParseDocumentAsync(html);
    }
}
