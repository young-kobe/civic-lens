using System.Data;
using CivicLens.PublicApi.Votes;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using Npgsql;

namespace CivicLens.PublicApi.Officials;

/// <summary>
/// Reads everything of one official page inside one repeatable-read snapshot, so the figures, the strip, and the lists come from one release.
/// The official figures come from <see cref="OfficialStore"/>, the recent votes from <see cref="MemberVoteStore"/>, and the rest from <see cref="OfficialPageQueries"/>.
/// </summary>
public sealed class OfficialPageReader(NpgsqlDataSource views)
{
    /// <summary>Reads the page. Null means that no release is active or that the active release names no such official.</summary>
    public async Task<OfficialPageData?> ReadAsync(string officialId, CancellationToken cancellationToken)
    {
        await using var connection = await views.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        if (await OfficialStore.ReadDetailAsync(connection, transaction, officialId, cancellationToken) is not { } detail) return null;
        var page = Profile(detail);
        return detail.ExecutiveActions is { } executive
            ? page with { Executive = await OfficialPageQueries.ReadExecutiveAsync(connection, transaction, officialId, executive, cancellationToken) }
            : await ReadLegislativeAsync(connection, transaction, page, detail, cancellationToken);
    }

    private static async Task<OfficialPageData> ReadLegislativeAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, OfficialPageData page,
        OfficialDetail detail, CancellationToken cancellationToken)
    {
        var id = page.OfficialId;
        var recent = await MemberVoteStore.ReadAsync(connection, transaction, new() { Officials = [id] }, cancellationToken);
        var marks = detail.Congress is { } congress
            ? await OfficialPageQueries.ReadMarksAsync(connection, transaction, id, congress, cancellationToken)
            : [];
        var bills = detail.Bills is { } figure
            ? await OfficialPageQueries.ReadBillsAsync(connection, transaction, id, figure.Congress, cancellationToken)
            : [];
        return page with
        {
            Votes = [.. (recent?.Officials[0].Votes ?? []).Take(OfficialPageQueries.RowLimit).Select(Vote)],
            Marks = marks,
            Statements = await OfficialPageQueries.ReadStatementsAsync(connection, transaction, id, cancellationToken),
            Bills = bills
        };
    }

    private static OfficialPageData Profile(OfficialDetail detail)
    {
        var official = detail.Official;
        return new(detail.Release, official.OfficialId, official.Name, official.Kind, official.Office, official.State, official.District, official.Party,
            official.Caucus, official.TermEndsOn, official.PhotoPath)
        {
            Attendance = [.. detail.MissedVotes.Select(figure => new OfficialAttendanceData(figure.Chamber, figure.Congress, figure.Missed, figure.Held,
                figure.FirstVotedOn, figure.LastVotedOn))],
            PartyLines = [.. detail.PartyLines.Select(figure => new OfficialPartyLineData(figure.Chamber, figure.Congress, figure.Party, figure.WithParty,
                figure.AgainstParty, figure.Cast, figure.FirstVotedOn, figure.LastVotedOn))],
            BillsCongress = detail.Bills?.Congress,
            SponsoredBills = detail.Bills?.Sponsored ?? 0,
            Issues = [.. (detail.Issues?.Issues ?? []).Where(issue => issue.Name is not null)
                .Select(issue => new OfficialIssueData(issue.IssueId, issue.Name!, issue.SponsoredBills + issue.VotesCast))]
        };
    }

    private static OfficialVoteData Vote(MemberVote vote) =>
        new(vote.RecordId, vote.VotedOn, vote.Chamber, vote.RollCall, vote.Headline, vote.ResultWords, vote.Cast, vote.CastWords);
}
