using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using CivicLens.Publication.Contracts;
using CivicLens.PublicWeb.Publication;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CivicLens.Tests.PublicWeb;

/// <summary>A House passage vote with whole-party counts, built so that every citation lies inside the one source text.</summary>
internal static class VoteDetailPageFixture
{
    public const string Root = "https://civic-lens.test/";
    public const string RecordId = "abababababababababababababababababababababababababababababababab";
    private const string Question = "On Passage";

    public static readonly string[] Lines =
    [
        "Started: 2026-09-16T12:00:00Z",
        "Question: " + Question,
        "Result: Passed",
        "Count: Yea 6, Nay 4",
        "Member: Ada Able, D, Aye",
        "Member: Ben Baker, D, Nay",
        "Member: Cy Cole, R, Aye",
        "Member: Di Dole, R, Nay",
        "Member: Ed Eaton, R, Not Voting"
    ];

    private static readonly string Text = string.Join('\n', Lines) + "\n";

    public static PublishedCitation Cite(string line) =>
        new() { ExtractionId = "ex1", Start = Text.IndexOf(line, StringComparison.Ordinal), Length = line.Length, Quote = line };

    public static readonly PublishedDocumentVersion Source = new()
    {
        ExtractionId = "ex1",
        Url = "https://final.example/vote",
        ObservedAtUtc = new(2026, 9, 17, 8, 0, 0, TimeSpan.Zero),
        Text = Text
    };

    public static readonly VoteMemberData[] Members =
    [
        new("o1", "Ada Able", "Aye", "Democrat", Cite(Lines[4])),
        new("o2", "Ben Baker", "Nay", "Democrat", Cite(Lines[5])),
        new("o3", "Cy Cole", "Aye", "Republican", Cite(Lines[6])),
        new("o4", "Di Dole", "Nay", "Republican", Cite(Lines[7])),
        new("o5", "Ed Eaton", "Not Voting", "Republican", Cite(Lines[8]))
    ];

    /// <summary>Democrats: 1 Yes, 3 No, 1 not voting. Republicans: 4 Yes, 1 No, 2 not voting. The Democratic majority is No and the Republican majority is Yes.</summary>
    public static readonly PublishedPartyTally[] Tallies =
    [
        new() { Party = "Republican", Yea = 4, Nay = 1, Present = 0, NotVoting = 2, Other = 0 },
        new() { Party = "Democrat", Yea = 1, Nay = 3, Present = 0, NotVoting = 1, Other = 0 },
        new() { Party = "Independent", Yea = 0, Nay = 1, Present = 1, NotVoting = 0, Other = 0 }
    ];

    public static VotePageData Vote() => new(7, RecordId, 2, PublishedChamber.House, 119, 2, 308, new(2026, 9, 16), PublishedVoteKind.Passage,
        PublishedVoteSource.CongressGov, "The House passed H.R. 5334.", "The House voted on a bill.", PublishedTextOrigin.Model, null, "Passed",
        PublishedVoteAlignment.Split, new() { Yea = 6, Nay = 4, Present = 1, NotVoting = 3 }, Tallies, Cite(Lines[0]), Cite(Lines[1]),
        [new() { Text = Lines[1], Citation = Cite(Lines[1]) }, new() { Text = Lines[2], Citation = Cite(Lines[2]) }, new() { Text = Lines[3], Citation = Cite(Lines[3]) }],
        [Source], Members, new("b".PadRight(64, 'b'), "Sanctioning Russia and Iran Act of 2026", "A bill line."), [new("tax", "Taxes")], "hr", 5334);

    public static async Task<IDocument> RenderAsync(VotePageData data, VoteMemberFilter? filter = null)
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<VoteDetailPage>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(VoteDetailPage.Data)] = data,
                [nameof(VoteDetailPage.RootPath)] = Root,
                [nameof(VoteDetailPage.Filter)] = filter ?? new VoteMemberFilter()
            }));
            return output.ToHtmlString();
        });
        return await new HtmlParser().ParseDocumentAsync(html);
    }
}
