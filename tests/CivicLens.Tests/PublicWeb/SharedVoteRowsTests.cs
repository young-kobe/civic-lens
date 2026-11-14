using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CivicLens.Tests.PublicWeb;

public sealed class SharedVoteRowsTests
{
    private const string Root = "https://civic-lens.test/";

    [Fact]
    public async Task RowsLinkVotesAndShowOnlyRecordedCastsInRequestedMemberOrder()
    {
        var row = new SharedVoteRowData(new string('a', 64), new(2026, 9, 16), 119, 2, 314,
            "House passes the bill", "AI summary of the official record", PublishedTextOrigin.Model, "Vote to pass with limited debate", "Passed",
            [new("second", "No (Nay)", "Nay"), new("first", "Yes (Yea)", "Yea")]);
        var data = new SharedVoteRowsData(PublishedChamber.House,
            [new("first", "Alex Member"), new("second", "Sam Member"), new("third", "Missing Member")], [row]);

        var document = await RenderAsync(data);

        Assert.Equal("How your House members voted", document.QuerySelector("h2")!.TextContent);
        Assert.Equal(Root + PublicationProtocol.RecordPagePath(row.RecordId), document.QuerySelector("h3 a")!.GetAttribute("href"));
        Assert.Equal("2026-09-16", document.QuerySelector("time")!.GetAttribute("datetime"));
        Assert.Contains("roll call 314", document.QuerySelector(".shared-vote-meta")!.TextContent);
        Assert.Contains("119th Congress, session 2", document.QuerySelector(".shared-vote-meta")!.TextContent);
        Assert.Contains("Vote to pass with limited debate. Result: Passed", document.QuerySelector(".shared-vote-meta")!.TextContent);
        Assert.Contains("AI summary of the official record", document.QuerySelector(".shared-vote-meta")!.TextContent);
        var casts = document.QuerySelectorAll(".shared-vote-cast");
        Assert.Equal(["Alex Member", "Sam Member"], casts.Select(cast => cast.QuerySelector(".shared-vote-member")!.TextContent));
        Assert.Equal(["Yes (Yea)", "No (Nay)"], casts.Select(cast => cast.QuerySelector(".shared-vote-cast-value")!.TextContent));
        Assert.Equal(["yea", "nay"], document.QuerySelectorAll(".shared-vote-cast-value").Select(cast => cast.GetAttribute("data-side")));
        Assert.DoesNotContain("Missing Member", document.DocumentElement!.TextContent);
    }

    [Fact]
    public async Task MetadataRemainsWhenNoCastsAreRecordedAndStoredTextIsEscaped()
    {
        const string headline = "<script>bad()</script>";
        var row = new SharedVoteRowData(new string('b', 64), new(2026, 9, 16), 119, 1, 5,
            headline, null, PublishedTextOrigin.Model, "Vote to agree to the motion", "Question agreed to", []);
        var data = new SharedVoteRowsData(PublishedChamber.Senate, [new("member", "Pat Member")], [row]);

        var document = await RenderAsync(data);

        Assert.Equal("How your senators voted", document.QuerySelector("h2")!.TextContent);
        Assert.Equal(headline, document.QuerySelector("h3 a")!.TextContent);
        Assert.Empty(document.QuerySelectorAll("script"));
        Assert.Contains("roll call 5", document.QuerySelector(".shared-vote-meta")!.TextContent);
        Assert.Contains("Vote to agree to the motion. Result: Question agreed to", document.QuerySelector(".shared-vote-meta")!.TextContent);
        Assert.Empty(document.QuerySelectorAll(".shared-vote-cast"));
        Assert.DoesNotContain("Not Voting", document.DocumentElement!.TextContent);
        Assert.Equal("Up to 10 recent published votes on passage, amendments, conference reports, veto overrides and nominations.",
            document.QuerySelector(".shared-vote-caption")!.TextContent);
        Assert.Null(document.QuerySelector(".shared-vote-comparison-caption"));
    }

    [Fact]
    public async Task ComparisonLabelsRenderOnlyWhenTheComparisonIsEligible()
    {
        var same = new SharedVoteRowData(new string('c', 64), new(2026, 9, 16), 119, 2, 7,
            "Members agree", null, PublishedTextOrigin.Model, "Vote to pass", "Passed", [new("first", "Yes (Yea)", "Yea"), new("second", "Yes (Aye)", "Yea")])
        {
            MembersVotedTheSame = true
        };
        var different = same with
        {
            RecordId = new string('d', 64),
            Casts = [new("first", "Yes (Yea)", "Yea"), new("second", "No (Nay)", "Nay")],
            MembersVotedTheSame = false
        };
        var noComparison = same with
        {
            RecordId = new string('e', 64),
            Casts = [new("first", "Yes (Yea)", "Yea")],
            MembersVotedTheSame = null
        };
        var document = await RenderAsync(new(PublishedChamber.House, [new("first", "Alex"), new("second", "Sam")], [same, different, noComparison]));

        Assert.Equal(["Same vote", "Voted differently"], document.QuerySelectorAll(".shared-vote-comparison").Select(node => node.TextContent));
        Assert.Equal("The comparison includes 2 votes where every listed member cast Yes or No. Their votes differed on 1.",
            document.QuerySelector(".shared-vote-comparison-caption")!.TextContent);
        Assert.Empty(document.QuerySelectorAll(".shared-vote-counts"));
        Assert.DoesNotContain("Not Voting", document.DocumentElement!.TextContent);
    }

    [Fact]
    public async Task ASingleMemberRowShowsNoComparisonBadgeEvenWhenTheGroupComparedTheVote()
    {
        var different = new SharedVoteRowData(new string('f', 64), new(2026, 9, 16), 119, 2, 8,
            "Members differ", null, PublishedTextOrigin.Model, "Vote to pass", "Passed", [new("first", "Yes (Yea)", "Yea")])
        {
            MembersVotedTheSame = false
        };

        var document = await RenderAsync(new(PublishedChamber.House, [new("first", "Alex")], [different]), singleMember: true);

        Assert.Single(document.QuerySelectorAll(".shared-vote-row"));
        Assert.Empty(document.QuerySelectorAll(".shared-vote-comparison"));
        Assert.DoesNotContain("Voted differently", document.DocumentElement!.TextContent);
    }

    [Fact]
    public async Task DifferencesOnlyKeepsTheOriginalComparisonCaptionAndShowsTheApprovedEmptyCopy()
    {
        var same = new SharedVoteRowData(new string('a', 64), new(2026, 9, 16), 119, 2, 7,
            "Same", null, PublishedTextOrigin.Model, "Vote to pass", "Passed", [new("first", "Yes (Yea)", "Yea"), new("second", "Yes (Yea)", "Yea")])
        {
            MembersVotedTheSame = true
        };
        var different = same with
        {
            RecordId = new string('b', 64),
            Headline = "Different",
            Casts = [new("first", "Yes (Yea)", "Yea"), new("second", "No (Nay)", "Nay")],
            MembersVotedTheSame = false
        };
        var data = new SharedVoteRowsData(PublishedChamber.Senate, [new("first", "Alex"), new("second", "Sam")], [same, different]);

        var document = await RenderAsync(data, differencesOnly: true);

        Assert.Equal("Votes where your members differed", document.QuerySelector("h2")!.TextContent);
        Assert.Equal(["Different"], document.QuerySelectorAll(".shared-vote-row h3").Select(node => node.TextContent));
        Assert.Equal("The comparison includes 2 votes where every listed member cast Yes or No. Their votes differed on 1.",
            document.QuerySelector(".shared-vote-comparison-caption")!.TextContent);

        var empty = await RenderAsync(new(PublishedChamber.House, data.Members, [same]), differencesOnly: true);
        Assert.Equal("No differing votes in this selection.", empty.QuerySelector("[data-member-votes-empty]")!.TextContent);
    }

    [Fact]
    public async Task TheHeadingLevelMovesTheSectionHeadingAndItsRowHeadingsTogether()
    {
        var row = new SharedVoteRowData(new string('d', 64), new(2026, 9, 16), 119, 1, 5, "A vote", null, PublishedTextOrigin.Model, "Vote to pass", "Passed", []);
        var data = new SharedVoteRowsData(PublishedChamber.House, [new("member", "Pat Member")], [row]);

        var nested = await RenderAsync(data, headingLevel: 3);
        var normal = await RenderAsync(data);

        Assert.Equal("How your House members voted", nested.QuerySelector("h3")!.TextContent);
        Assert.Equal("A vote", nested.QuerySelector("h4 a")!.TextContent);
        Assert.Null(nested.QuerySelector("h2"));
        Assert.Equal("How your House members voted", normal.QuerySelector("h2")!.TextContent);
        Assert.Equal("A vote", normal.QuerySelector("h3 a")!.TextContent);
    }

    [Fact]
    public async Task TheRowLabelIsDrawnByAiLabel()
    {
        var row = new SharedVoteRowData(new string('a', 64), new(2026, 9, 16), 119, 2, 314, "House passes the bill", AiLabelWords.ModelWording,
            PublishedTextOrigin.Model, "Vote to pass", "Passed", []);

        AiLabelAssert.DrawnByAiLabel(await RenderAsync(new(PublishedChamber.House, [], [row])), AiLabelWords.ModelWording);
    }

    [Fact]
    public async Task OnlyAModelLabelTakesTheAiInkAndACodeLabelStaysMutedAsInAListRow()
    {
        var model = new SharedVoteRowData(new string('a', 64), new(2026, 9, 16), 119, 2, 314, "House passes the bill", AiLabelWords.ModelWording,
            PublishedTextOrigin.Model, "Vote to pass", "Passed", []);
        var code = model with { RecordId = new string('b', 64), Label = AiLabelWords.CodeWording, TextOrigin = PublishedTextOrigin.Code };

        var labels = (await RenderAsync(new(PublishedChamber.House, [], [model, code]))).QuerySelectorAll(".shared-vote-meta .ai-label");

        Assert.Equal(["model", "code"], labels.Select(label => label.GetAttribute("data-origin")));
        Assert.Equal([AiLabelWords.ModelWording, AiLabelWords.CodeWording], labels.Select(label => label.TextContent));
        Assert.DoesNotMatch(@"(?m)^\.shared-vote-[^{]*\{[^}]*color: var\(--ai-ink\)", WebStyleSheets.Read("public.css"));
    }

    private static async Task<IDocument> RenderAsync(SharedVoteRowsData data, bool differencesOnly = false, int headingLevel = 2, bool singleMember = false)
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<SharedVoteRows>(ParameterView.FromDictionary(
                new Dictionary<string, object?>
                {
                    [nameof(SharedVoteRows.Data)] = data,
                    [nameof(SharedVoteRows.RootPath)] = Root,
                    [nameof(SharedVoteRows.DifferencesOnly)] = differencesOnly,
                    [nameof(SharedVoteRows.HeadingLevel)] = headingLevel,
                    [nameof(SharedVoteRows.SingleMember)] = singleMember
                }));
            return output.ToHtmlString();
        });
        return new HtmlParser().ParseDocument(html);
    }
}
