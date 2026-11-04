using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.Host.Publication;

internal static class PublishedOfficialSectionsView
{
    public const int ItemCount = 5;

    public const string IssuesId = "issues";

    public const string JumpLabel = "On this page";

    public const string NoBreaks = "No votes against most of their party.";

    private const string NoBills = "No bills on record.";

    private const string NoVotes = "No votes on record.";

    private const string NoStatements = "No statements on record.";

    public static string RecordPageLink(ReleaseOfficialPage page, string? tab = null, string? issue = null)
    {
        var query = new[] { tab is null ? null : $"tab={tab}", issue is null ? null : $"issue={issue}" }.OfType<string>().ToList();
        return page.RootPath + PublicationProtocol.OfficialRecordPagePath(page.Profile.Id) + (query.Count == 0 ? "" : "?" + string.Join('&', query));
    }

    public static string AllRecordsText(int count) => $"See all {PublishedOfficialView.Counted(count, "record")}";

    public static IReadOnlyList<OfficialSection> Of(ReleaseOfficialPage page, IReadOnlyList<PublishedOfficialRow> rows)
    {
        var profile = page.TalkAndAction;
        var bills = rows.Where(row => row.Kind == PublishedRecordKind.Action).ToList();
        var sections = new List<OfficialSection>();
        if (profile is not null && profile.WithPartyByChamber.Any(votes => votes.Cast > 0))
            sections.Add(Latest(page, "break", "Where they break from their party", rows.Where(row => row.AgainstParty).ToList(), PublishedOfficialView.AgainstPartyClass,
                count => $"See all {PublishedOfficialView.Counted(count, "vote")} against their party", NoBreaks));
        if (profile is not null || bills.Count > 0)
            sections.Add(Latest(page, "bills", "Bills they introduced", bills, PublishedOfficialView.KindClass(PublishedRecordKind.Action),
                count => $"See all {PublishedOfficialView.Counted(count, "bill")} they introduced", NoBills));
        if (profile is not null)
        {
            sections.Add(new(IssuesId, PublishedOfficialView.IssuesTabLabel, [], profile.IssueIds.Length,
                profile.IssueIds.IsEmpty ? null : $"See the counts for all {PublishedOfficialView.Counted(profile.IssueIds.Length, "issue")}",
                RecordPageLink(page, PublishedOfficialView.IssuesPane), PublishedOfficialView.NoTrackedIssue));
            sections.Add(Latest(page, "votes", "How they voted recently", rows.Where(row => row.Kind == PublishedRecordKind.Vote).ToList(),
                PublishedOfficialView.KindClass(PublishedRecordKind.Vote), count => $"See all {PublishedOfficialView.Counted(count, "vote")}", NoVotes));
        }
        sections.Add(Latest(page, "statements", "What they said", rows.Where(row => row.Kind == PublishedRecordKind.Statement).ToList(),
            PublishedOfficialView.KindClass(PublishedRecordKind.Statement), count => $"See all {PublishedOfficialView.Counted(count, "statement")}", NoStatements));
        return sections;
    }

    private static OfficialSection Latest(ReleaseOfficialPage page, string id, string title, List<PublishedOfficialRow> matching, string tab,
        Func<int, string> seeAll, string empty) =>
        new(id, title, [.. matching.Take(ItemCount)], matching.Count, matching.Count == 0 ? null : seeAll(matching.Count), RecordPageLink(page, tab), empty);
}
