using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Home;

/// <summary>Maps the current public home answer to the public presentation contract without rereading its release.</summary>
public sealed class HomePageReader(HomeStore home, PublicBaseUrl? browserApiUrl)
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
            Issues = issues
        };
    }
}
