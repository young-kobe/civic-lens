using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Http;
using CivicLens.PublicApi.Issues;
using CivicLens.PublicWeb.Publication;
using Microsoft.AspNetCore.Http;

namespace CivicLens.PublicApi.Votes;

/// <summary>Maps one page of the vote list to the presentation record of the server page. It reads through <see cref="VoteStore"/> and <see cref="IssueStore"/>, as the JSON routes do.
/// The page takes the bill as one typed number, such as "H.R. 5334", and always holds <see cref="PageSize"/> rows.</summary>
public sealed class VoteListReader(VoteStore votes, IssueStore issues)
{
    public const int PageSize = 25;

    private static readonly SearchChoiceData[] Kinds =
        [.. Enum.GetValues<PublishedVoteKind>().Select(kind => new SearchChoiceData(QueryValues.ChoiceText(kind), PlainVoteWords.Kind(kind)))];

    /// <summary>Reads the query string. An invalid value throws <see cref="ArgumentException"/> that names the parameter.</summary>
    public static VoteRequest Parse(IQueryCollection query)
    {
        var (billType, billNumber) = QueryValues.BillNumber(query, "bill");
        return VoteRequest.Parse(query) with { Congress = null, Session = null, BillType = billType, BillNumber = billNumber, Limit = PageSize };
    }

    public async Task<VoteListPageData?> ReadAsync(VoteRequest request, CancellationToken cancellationToken)
    {
        if (await votes.ListAsync(request, cancellationToken) is not { } page) return null;
        var choices = (await issues.ListOptionsAsync(cancellationToken)).Select(issue => issue.ToChoice()).ToArray();
        var rows = page.Votes.Select(vote => new VoteListRowData(vote.RecordId, vote.Headline, vote.Label, vote.Chamber, vote.RollCall, vote.VotedOn, vote.Kind,
            vote.BillType is { } type && vote.BillNumber is { } number ? BillNumbers.Of(type, number) : null, vote.Result, vote.Alignment, vote.Yea, vote.Nay)).ToArray();
        var filter = RecordListFilter.Of(("chamber", request.Chamber is { } chamber ? chamber.ToString().ToLowerInvariant() : null),
            ("kind", request.Kind is { } kind ? QueryValues.ChoiceText(kind) : null), ("issue", request.Issue), ("bill", QueryValues.BillNumberText(request.BillType, request.BillNumber)),
            ("from", request.From?.ToString("yyyy-MM-dd")), ("to", request.To?.ToString("yyyy-MM-dd")));
        return new(page.Release, rows, filter, page.NextCursor, Kinds, choices);
    }
}
