using CivicLens.PublicApi.Boards;
using CivicLens.PublicApi.Votes;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Home;

/// <summary>
/// Maps the current public home answer to the public presentation contract. The featured vote page and the party boards come from their own readers.
/// Each one joins the page only when it read the same release as the home answer, so the page never mixes two releases.
/// </summary>
public sealed class HomePageReader(HomeStore home, VotePageReader votes, BoardsPageReader boards, PublicBaseUrl? browserApiUrl)
{
    public string? BrowserApiRoot => browserApiUrl?.Join(string.Empty).TrimEnd('/');

    public async Task<HomePageData?> ReadAsync(CancellationToken cancellationToken)
    {
        if (await home.ReadAsync(cancellationToken) is not { } answer) return null;
        var featured = answer.FeaturedVote is { } vote
            ? new HomeFeaturedVoteData(vote.RecordId, vote.VotedOn, vote.Chamber, vote.Headline, vote.Label, vote.Summary,
                vote.TextOrigin, vote.ResultWords, vote.Yea, vote.Nay, vote.BillLine, vote.BillLineSource)
            : null;
        var votes = answer.Votes.Select(vote => new HomeVoteData(vote.RecordId, vote.VotedOn, vote.Chamber, vote.Headline, vote.Label,
            vote.ResultWords, vote.Yea, vote.Nay, vote.BillLine)
        { Alignment = vote.Alignment }).ToArray();
        var brief = answer.Brief is { } item ? new HomeBriefData(item.RecordId, item.Headline, item.Label) : null;
        var executiveActions = answer.ExecutiveActions.Select(group => new HomeExecutiveGroupData(
            group.Kind, group.Count, group.NewestRecordId, group.NewestTitle, group.NewestSignedOn)).ToArray();
        var issues = answer.Issues.Select(issue => new HomeIssueData(
            issue.IssueId, issue.Name, issue.Congress, issue.SubstantiveVotes, issue.Bills)).ToArray();
        return new(answer.Release, featured, votes, brief)
        {
            ExecutiveActions = executiveActions,
            Issues = issues,
            NewestHouseRollCall = answer.NewestHouseRollCall,
            NewestSenateRollCall = answer.NewestSenateRollCall
        };
    }

    /// <summary>
    /// Adds the featured vote page and the four party boards. They are the costly reads, so the home route calls this only when it renders, not for a 304.
    /// Each one joins only when it read the release of the home answer.
    /// </summary>
    public async Task<HomePageData> WithDetailAsync(HomePageData page, CancellationToken cancellationToken)
    {
        var detail = page.FeaturedVote is { } featured ? await ReadFeaturedVoteAsync(featured.RecordId, page.Release, cancellationToken) : null;
        var panels = await boards.ReadAsync(cancellationToken) is { } board && board.Release == page.Release ? board.Boards : [];
        return page with { FeaturedVoteDetail = detail, Boards = panels };
    }

    /// <summary>
    /// The vote page data of the featured vote. A vote whose members lack official names cannot draw a map, and the vote page would refuse it too,
    /// so Home keeps its own answer and omits the map and party bars instead of failing.
    /// </summary>
    private async Task<VotePageData?> ReadFeaturedVoteAsync(string recordId, int release, CancellationToken cancellationToken)
    {
        try
        {
            return await votes.ReadAsync(recordId, cancellationToken) is { } vote && vote.Release == release ? vote : null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }
}
