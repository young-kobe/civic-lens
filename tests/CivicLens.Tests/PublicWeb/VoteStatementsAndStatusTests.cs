using CivicLens.Publication.Contracts;
using CivicLens.PublicWeb.Publication;
using static CivicLens.Tests.PublicWeb.VoteDetailPageFixture;

namespace CivicLens.Tests.PublicWeb;

public sealed class VoteStatementsAndStatusTests
{
    private static VoteStatementData Statement(string id, PublishedApprover? approver, params (string Id, string Name)[] speakers) =>
        new(id.PadRight(64, 'c'), new DateOnly(2026, 9, 12), "A statement about the bill", null, PublishedTextOrigin.Person, approver, [.. speakers.Select(speaker => new VoteStatementSpeaker(speaker.Id, speaker.Name))]);

    [Fact]
    public async Task NoLinkedStatementShowsNoSection()
    {
        var page = await RenderAsync(Vote());

        Assert.Null(page.QuerySelector(".vote-statements"));
        Assert.DoesNotContain("Statements linked to this vote", page.QuerySelector("main")!.TextContent);
    }

    [Fact]
    public async Task TheSectionHasTheApprovedIntroAndNoteAndLeavesOutTheMockupSentenceThatTheLabelsReplace()
    {
        var page = await RenderAsync(Vote() with { Statements = [Statement("a", PublishedApprover.Person, ("o1", "Ada Able"))], StatementTotal = 1 });

        var section = page.QuerySelector(".vote-statements")!;
        Assert.Equal("Statements linked to this vote", section.QuerySelector("h2")!.TextContent);
        Assert.Contains("Statements by members about H.R. 5334.", section.TextContent);
        Assert.Contains("How to read this", section.TextContent);
        Assert.Contains("A linked statement is the record's own words. It is not a reason Civic Lens gives for the vote.", section.TextContent);
        Assert.DoesNotContain("A reviewer linked each one to this bill.", section.TextContent);
        Assert.Null(section.QuerySelector(".muted.small"));
    }

    [Fact]
    public async Task EachLinkNamesWhoCheckedItAndALinkWithNoApproverYetShowsNoLabel()
    {
        var page = await RenderAsync(Vote() with
        {
            Statements =
            [
                Statement("a", PublishedApprover.Code, ("o1", "Ada Able")),
                Statement("b", PublishedApprover.Person, ("o2", "Ben Baker")),
                Statement("c", null, ("o3", "Cy Cole"))
            ],
            StatementTotal = 3
        });

        var rows = page.QuerySelectorAll(".vote-statements li").Select(row => row.QuerySelector(".link-checker")?.TextContent).ToArray();
        Assert.Equal(new string?[] { "Code checked this link", "A reviewer checked this link", null }, rows);
    }

    [Fact]
    public async Task ARowShowsTheCastOfEachSpeakerWhoHasAVoteRowAndNoneForOthers()
    {
        var page = await RenderAsync(Vote() with { Statements = [Statement("a", PublishedApprover.Code, ("o1", "Ada Able"), ("zz", "Zed Zane"))], StatementTotal = 1 });

        var casts = page.QuerySelectorAll(".vote-statements li .official-related").Where(line => line.TextContent.Contains("Cast on this vote")).ToArray();
        var cast = Assert.Single(casts);
        Assert.Contains("Ada Able", cast.TextContent);
        Assert.Contains("Yes", cast.TextContent);
    }

    [Fact]
    public async Task ACutListSaysHowManyAreLinkedAndAFullListSaysNothing()
    {
        var one = Statement("a", PublishedApprover.Code, ("o1", "Ada Able"));

        var cut = await RenderAsync(Vote() with { Statements = [one], StatementTotal = 14 });
        var full = await RenderAsync(Vote() with { Statements = [one], StatementTotal = 1 });

        Assert.Contains("The 1 newest of 14.", cut.QuerySelector(".vote-statements")!.TextContent);
        Assert.DoesNotContain("newest of", full.QuerySelector(".vote-statements")!.TextContent);
    }

    [Fact]
    public async Task AVoteNotOnABillHasNoIntroSentence()
    {
        var page = await RenderAsync(Vote() with { BillType = null, BillNumber = null, Statements = [Statement("a", PublishedApprover.Code, ("o1", "Ada Able"))], StatementTotal = 1 });

        Assert.DoesNotContain("Statements by members about", page.QuerySelector(".vote-statements")!.TextContent);
    }

    [Fact]
    public async Task TheBillStatusBadgeUsesTheStatusLabelOwnerAndIsAbsentWhenNoRuleMatches()
    {
        var bill = Vote().Bill!;
        var matched = await RenderAsync(Vote() with { Bill = bill with { StatusLabel = BillStatusMeaning.Label("Placed on the Union Calendar, Calendar No. 12.", "hr") } });
        var unmatched = await RenderAsync(Vote() with { Bill = bill with { StatusLabel = BillStatusMeaning.Label("Some text that no rule matches.", "hr") } });

        Assert.Equal("Waiting for a possible House floor vote", matched.QuerySelector(".vote-bill .badge")!.TextContent.Trim());
        Assert.Null(unmatched.QuerySelector(".vote-bill .badge"));
        Assert.Null(unmatched.QuerySelector(".vote-bill-status"));
    }

    [Fact]
    public async Task TheLawLineShowsOnlyForABillThatBecameLaw()
    {
        var bill = Vote().Bill!;
        var law = await RenderAsync(Vote() with
        {
            Bill = bill with { StatusLabel = "Became law", PublicLaw = "119-111", StatusDate = new(2026, 9, 18), StatusReadAtUtc = new DateTimeOffset(2026, 11, 5, 9, 0, 0, TimeSpan.Zero) }
        });
        var other = await RenderAsync(Vote() with { Bill = bill with { StatusLabel = "In committee", StatusDate = new(2026, 9, 18), StatusReadAtUtc = new DateTimeOffset(2026, 11, 5, 9, 0, 0, TimeSpan.Zero) } });

        Assert.Contains("The bill became law as Public Law 119-111 on 18 Sep 2026, according to Congress.gov, read on 5 Nov 2026.", law.QuerySelector(".vote-explain")!.TextContent);
        Assert.DoesNotContain("became law", other.QuerySelector(".vote-explain")!.TextContent);
    }
}
