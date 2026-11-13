using CivicLens.PublicApi.Votes;
using CivicLens.PublicWeb.Components.Ui;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Bills;

/// <summary>
/// Reads what the bill page shows and maps it to the public presentation contract. Every part comes from the same active release as the bill.
/// When a release change splits the parts, the read starts again. Each part is one read that no other part repeats.
/// </summary>
public sealed class BillPageReader(BillStore bills, VoteStore votes)
{
    private const int ActionsPerRead = 500;
    private const int Attempts = 3;

    /// <summary>Reads the page of one bill. Null means that no active release holds the bill.</summary>
    public async Task<BillPageData?> ReadAsync(string recordId, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            if (await bills.FindAsync(recordId, cancellationToken) is not { } detail) return null;
            if (await ReadPartsAsync(detail, cancellationToken) is { } page) return page;
        }
        throw new InvalidOperationException("The active release changed on every read of the bill page.");
    }

    private async Task<BillPageData?> ReadPartsAsync(BillDetail detail, CancellationToken cancellationToken)
    {
        var bill = detail.Bill;
        if (await ReadActionsAsync(bill.RecordId, detail.Release, cancellationToken) is not { } actions) return null;
        if (await bills.ListSponsorsAsync(bill.RecordId, cancellationToken) is not { } sponsors || sponsors.Release != detail.Release) return null;
        if (await ReadIssuesAsync(detail, cancellationToken) is not { } issues) return null;
        var voteRequest = new VoteRequest { Congress = bill.Congress, BillType = bill.BillType, BillNumber = bill.BillNumber, Limit = PublicationProtocol.PageSize };
        if (await votes.ListAsync(voteRequest, cancellationToken) is not { } voteRows || voteRows.Release != detail.Release) return null;
        // The shown list stops at one page, so the gap note reads the chambers from a read that is complete for the bill.
        var chambers = await votes.FindBillChambersAsync(bill.Congress, bill.BillType, bill.BillNumber, cancellationToken);
        if (chambers is null || chambers.Release != detail.Release) return null;
        return Map(detail, actions, sponsors.Rows, issues, voteRows.Votes, chambers.Chambers);
    }

    private async Task<BillActionRow[]?> ReadActionsAsync(string recordId, int release, CancellationToken cancellationToken)
    {
        var rows = new List<BillActionRow>();
        ActionHistoryCursor? cursor = null;
        do
        {
            var page = await bills.ListActionsAsync(new() { RecordId = recordId, Cursor = cursor, Limit = ActionsPerRead }, cancellationToken);
            if (page is null || page.Release != release) return null;
            rows.AddRange(page.Actions);
            cursor = ActionHistoryCursor.Parse(page.NextCursor);
        }
        while (cursor is not null);
        return [.. rows];
    }

    private async Task<BillIssueData[]?> ReadIssuesAsync(BillDetail detail, CancellationToken cancellationToken)
    {
        if (detail.IssueIds.Length == 0) return [];
        var names = await bills.ListIssueNamesAsync(detail.IssueIds, cancellationToken);
        return names is { } read && read.Release == detail.Release ? read.Issues : null;
    }

    private static BillPageData Map(BillDetail detail, BillActionRow[] actions, BillSponsorRow[] sponsorships, BillIssueData[] issues, VoteRow[] voteRows,
        PublishedChamber[] votedChambers)
    {
        var bill = detail.Bill;
        var chamber = BillNumbers.ChamberOf(bill.BillType);
        var sponsors = sponsorships.Where(row => row.Role == PublishedSponsorshipRole.Sponsor).Select(Person).ToArray();
        var cosponsors = sponsorships.Where(row => row.Role == PublishedSponsorshipRole.Cosponsor && row.WithdrawnOn is null).ToArray();
        var steps = BillSteps.Of(bill, sponsors.FirstOrDefault()?.Name, actions);
        return new(detail.Release, bill.RecordId, bill.RevisionNumber, bill.Congress, BillNumbers.Of(bill.BillType, bill.BillNumber), bill.BillTitle, bill.Headline,
            chamber, bill.Summary, bill.TextOrigin, bill.Label, bill.EventDate)
        {
            BillLine = bill.BillLine,
            PolicyArea = bill.PolicyArea,
            PublicLaw = bill.PublicLaw,
            BecameLawOn = BillSteps.LawAction(actions)?.ActionDate,
            Status = detail.LatestAction is { } latest ? BillStatusMeaning.Of(latest.Text, bill.BillType) : null,
            StatusLabel = detail.LatestAction is { } labeled ? BillStatusMeaning.Label(labeled.Text, bill.BillType) : null,
            StatusDate = detail.LatestAction?.Date,
            StatusReadAtUtc = detail.LatestAction?.ReadAtUtc,
            Sponsors = sponsors,
            Cosponsors = new(chamber, [.. cosponsors.Where(row => row.IsOriginal).Select(Person)], [.. cosponsors.Where(row => !row.IsOriginal).Select(Person)]),
            Steps = steps,
            Votes = [.. voteRows.Select(VoteRowOf)],
            SenateVoteGap = BillSteps.SenateActedAfterIntroduction(steps) && !votedChambers.Contains(PublishedChamber.Senate),
            Issues = issues,
            CrsText = detail.CrsSummaryText,
            CrsVersion = detail.CrsSummaryVersion,
            SummaryFromCrs = detail.SummaryFromCrs,
            CheckedAtUtc = detail.CheckedAtUtc,
            FirstPublishedAtUtc = detail.FirstPublishedAtUtc,
            CongressGovUrl = CongressGovBillPage.Url(bill.Congress, bill.BillType, bill.BillNumber)
        };
    }

    /// <summary>The chamber bar uses the shared cast bar of the vote page with no half line. The source gives no counts for a vote for a candidate, so that row has no bar.</summary>
    private static SharedVoteRowData VoteRowOf(VoteRow vote) => new(vote.RecordId, vote.VotedOn, vote.Congress, vote.Session, vote.RollCall, vote.Headline, vote.Label,
        PlainVoteWords.Kind(vote.Kind), PlainVoteWords.Result(vote.Result), [])
    {
        Alignment = vote.Alignment,
        Totals = vote is { Yea: { } yes, Nay: { } no, Present: { } present, NotVoting: { } notVoting } && yes + no + present + notVoting > 0
            ? VotePageView.CastBar($"{vote.Chamber} members", PartyTone.Neutral, yes, no, present, notVoting, 0, VoteThreshold.None)
            : null
    };

    private static BillSponsorData Person(BillSponsorRow row) => new(row.OfficialId, row.Name, row.Party, row.SponsoredOn);
}
