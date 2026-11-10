using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CivicLens.Tests.PublicWeb;

public sealed class MembersStripTests
{
    [Fact]
    public async Task RendersDistinctOccupiedMembersWithPublicLinksAndEscapedNames()
    {
        var first = Member("senator/1", "A <script>alert(1)</script>");
        var duplicate = Member("senator/1", "Repeated identity");
        var second = Member("representative", "Second Member");
        var data = new ZipMemberCardsData(8, 119, "36009", 2,
            [new("Senator", first), new("Senator", duplicate), new("Representative", second), new("Vacant", null)], []);

        var document = await RenderAsync(data, "/public/");

        var nav = document.QuerySelector("nav[aria-label='Your members']")!;
        var links = nav.QuerySelectorAll("a");
        Assert.Equal(["Your members", first.Name, second.Name], links.Select(link => link.TextContent));
        Assert.Equal("/public/" + PublicationProtocol.HomePath + "#zip-results", links[0].GetAttribute("href"));
        Assert.Equal("/public/" + PublicationProtocol.OfficialPagePath(Uri.EscapeDataString(first.OfficialId)), links[1].GetAttribute("href"));
        Assert.Equal("/public/" + PublicationProtocol.OfficialPagePath("representative"), links[2].GetAttribute("href"));
        Assert.All(links, link => Assert.DoesNotContain("36009", link.GetAttribute("href")));
        Assert.Empty(document.QuerySelectorAll("script"));
        Assert.Equal("Forget", nav.QuerySelector("button[data-forget-members]")!.TextContent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    public async Task OmitsNoticeWithoutPositiveCount(long? count)
    {
        var document = await RenderAsync(new(8, 119, "36009", 0, [], []), newVoteCount: count);

        Assert.Null(document.QuerySelector("[data-member-updates]"));
    }

    [Theory]
    [InlineData(1, "1 newly published vote since your last member lookup.")]
    [InlineData(4, "4 newly published votes since your last member lookup.")]
    public async Task RendersApprovedNoticeCopyWithCorrectPluralization(long count, string expected)
    {
        var document = await RenderAsync(new(8, 119, "36009", 0, [], []), newVoteCount: count);

        Assert.Equal(expected, document.QuerySelector("[data-member-updates]")!.TextContent);
    }

    private static ZipMemberIdentityData Member(string officialId, string name) =>
        new(officialId, name, null, null, null, [], []);

    private static async Task<IDocument> RenderAsync(ZipMemberCardsData data, string rootPath = "", long? newVoteCount = null)
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<MembersStrip>(ParameterView.FromDictionary(
                new Dictionary<string, object?>
                {
                    [nameof(MembersStrip.Data)] = data,
                    [nameof(MembersStrip.RootPath)] = rootPath,
                    [nameof(MembersStrip.NewVoteCount)] = newVoteCount
                }));
            return output.ToHtmlString();
        });
        return new HtmlParser().ParseDocument(html);
    }
}
