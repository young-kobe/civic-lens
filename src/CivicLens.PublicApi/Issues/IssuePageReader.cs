using CivicLens.Publication.Contracts;
using CivicLens.PublicWeb.Publication;

namespace CivicLens.PublicApi.Issues;

/// <summary>Maps the page answer of one issue to the public presentation contract without rereading its release.</summary>
public sealed class IssuePageReader(IssueStore issues)
{
    /// <summary>Reads the page data. Null means that the active release holds no such issue, or that the id cannot name one.</summary>
    public async Task<IssuePageData?> ReadAsync(string issueId, CancellationToken cancellationToken)
    {
        if (issueId.Length > PublishedRecordEntry.MaximumFilterValueLength) return null;
        if (await issues.FindPageAsync(issueId, cancellationToken) is not { } answer) return null;
        var votes = answer.Votes.Select(vote => new IssueVoteData(vote.RecordId, vote.VotedOn, vote.Chamber, vote.Headline, vote.Label, vote.ResultWords, vote.Yea, vote.Nay)).ToArray();
        var bills = answer.Bills.Select(bill => new IssueBillData(bill.RecordId, bill.Headline, bill.Label, bill.EventDate, bill.BillLine, bill.Sponsor)).ToArray();
        var lineUp = answer.LineUp is { } split
            ? new IssueLineUpData(split.Congress, split.Bills, [.. split.Parties.Select(party => new IssueLineUpPartyData(party.Party, party.Bills))], split.UnknownBills)
            : null;
        return new(answer.Release, answer.IssueId, answer.Name, answer.Definition, answer.Includes, answer.Excludes, answer.Congress, answer.SubstantiveVotes, answer.SplitVotes, answer.BillCount, votes, bills, lineUp);
    }
}
