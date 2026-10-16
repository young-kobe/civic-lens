using System.Text;
using CivicLens.Application.Collection.HouseVotes;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Collection.HouseVotes;

public sealed class HouseVoteRendererTests
{
    [Fact]
    public void ADetailRendersEachFactOnItsOwnLineSoACitationCanQuoteExactlyOneFact()
    {
        var text = HouseVoteRenderer.RenderDetail(Detail());

        Assert.Equal("""
            House vote: 119 1 100
            Updated: 2025-06-24T12:34:41Z
            Started: 2025-04-10T15:08:00Z
            Question: On Motion to Concur in the Senate Amendment
            Vote type: Yea-and-Nay
            Result: Passed
            Legislation: HCONRES 14
            Party total D: Yea 1, Nay 212, Present 0, Not voting 2
            Party total R: Yea 216, Nay 2, Present 0, Not voting 3
            Source data: https://clerk.house.gov/evs/2025/roll100.xml
            """.ReplaceLineEndings("\n"), text);
    }

    [Fact]
    public void PartyTotalsRenderInPartyOrderWhateverOrderTheApiSent()
    {
        var detail = Detail();

        var reversed = detail with { PartyTotals = [.. detail.PartyTotals.Reverse()] };

        Assert.Equal(HouseVoteRenderer.RenderDetail(detail), HouseVoteRenderer.RenderDetail(reversed));
    }

    [Fact]
    public void ADetailWithoutOptionalFactsRendersNoEmptyLineForThem()
    {
        var bare = new HouseVoteDetail(HouseVoteFixtures.Vote(100, HouseVoteFixtures.Updated, null, null), null, null, null, null,
            null, null, [], null);

        Assert.Equal("House vote: 119 1 100\nUpdated: 2025-06-24T12:34:41Z", HouseVoteRenderer.RenderDetail(bare));
    }

    [Fact]
    public void MembersRenderOneLineEachSortedByBioguideIdWhateverOrderTheApiSent()
    {
        var members = Members();

        var text = HouseVoteRenderer.RenderMembers(members with { Members = [.. members.Members.Reverse()] });

        Assert.Equal("""
            House vote members: 119 1 100
            Updated: 2025-06-24T12:34:41Z
            Member: A000055, Robert Aderholt, R, AL, Yea
            Member: A000148, Jake Auchincloss, D, MA, Nay
            Member: A000369, Mark Amodei, R, NV, Not Voting
            """.ReplaceLineEndings("\n"), text);
        Assert.Equal(text, HouseVoteRenderer.RenderMembers(members));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\u2028")]
    [InlineData("\u2029")]
    public void ALineBreakInsideAValueCannotSplitOneFactIntoTwoLines(string lineBreak)
    {
        var member = new HouseVoteMember("A000055", $"Robert{lineBreak}Jr", "Aderholt", "R", "AL", "Yea");
        var votes = new HouseVoteMemberVotes(HouseVoteFixtures.Vote(100, HouseVoteFixtures.Updated), [member]);

        var lines = HouseVoteRenderer.RenderMembers(votes).Split(['\n', '\u2028', '\u2029']);

        Assert.Equal(3, lines.Length);
        Assert.Equal("Member: A000055, Robert Jr Aderholt, R, AL, Yea", lines[2]);
    }

    private static HouseVoteDetail Detail() =>
        HouseVoteParser.Parse(Encoding.UTF8.GetBytes(HouseVoteFixtures.Detail()), HouseVoteFixtures.Roll100);

    private static HouseVoteMemberVotes Members() =>
        HouseVoteMembersParser.Parse(Encoding.UTF8.GetBytes(HouseVoteFixtures.Members()), HouseVoteFixtures.Roll100);
}
