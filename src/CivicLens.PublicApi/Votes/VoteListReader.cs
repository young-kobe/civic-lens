using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Http;
using CivicLens.PublicApi.Issues;
using CivicLens.PublicApi.Records;
using CivicLens.PublicWeb.Publication;
using Microsoft.AspNetCore.Http;

namespace CivicLens.PublicApi.Votes;

/// <summary>Maps one page of the vote list to the presentation record of the server page. One snapshot holds the page, the total, and the issue choices,
/// so the page comes from one release, as the statement list reads it. The page takes the bill as one typed number, such as "H.R. 5334", and always holds <see cref="PageSize"/> rows.</summary>
public sealed class VoteListReader(VoteStore votes, RecordSnapshot snapshot)
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
        var (connection, transaction) = await snapshot.OpenAsync(cancellationToken);
        await using var _ = connection;
        await using var __ = transaction;
        if (await votes.ListWithTotalAsync(transaction, request, cancellationToken) is not var (page, total)) return null;
        var choices = (await IssueStore.ListOptionsAsync(connection, transaction, cancellationToken)).Select(issue => issue.ToChoice()).ToArray();
        var rows = page.Votes.Select(vote => new VoteListRowData(vote.RecordId, vote.Headline, vote.Label, vote.TextOrigin, vote.Chamber, vote.RollCall, vote.VotedOn, vote.Kind,
            vote.BillType is { } type && vote.BillNumber is { } number ? BillNumbers.Of(type, number) : null, vote.Result, vote.Alignment, vote.Yea, vote.Nay)).ToArray();
        var filter = RecordListFilter.Of(request.From, request.To, ("chamber", request.Chamber is { } chamber ? chamber.ToString().ToLowerInvariant() : null),
            ("kind", request.Kind is { } kind ? QueryValues.ChoiceText(kind) : null), ("issue", request.Issue), ("bill", QueryValues.BillNumberText(request.BillType, request.BillNumber)));
        return new(page.Release, total, rows, filter, page.NextCursor, Kinds, choices);
    }

    /// <summary>The page of a refused request: the choices and the total of the first page, the typed text of each field, and no rows.</summary>
    public async Task<VoteListPageData?> ReadRefusedAsync(IQueryCollection query, string? invalid, CancellationToken cancellationToken) =>
        await ReadAsync(Parse(QueryCollection.Empty), cancellationToken) is { } data
            ? data with { Votes = [], NextCursor = null, Filter = RecordListFilter.Refused(invalid, QueryValues.Typed(query, "chamber", "kind", "issue", "bill", "from", "to")) }
            : null;
}
