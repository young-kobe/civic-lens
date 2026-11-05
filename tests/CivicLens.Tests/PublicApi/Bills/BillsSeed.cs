using System.Globalization;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.PublicApi.Votes;

namespace CivicLens.Tests.PublicApi.Bills;

internal sealed record SeededSponsorship(string Person, string Role, string SponsoredOn, string? WithdrawnOn = null, bool IsOriginal = true);

/// <summary>A published bill as the operational tables hold it. The public views read these rows.</summary>
internal sealed record SeededBill(char Id, string EventOn)
{
    public int Congress { get; init; } = 119;
    public string BillType { get; init; } = "hr";
    public int BillNumber { get; init; } = 7;
    public string? PolicyAreaId { get; init; } = "health";
    public string TextOrigin { get; init; } = "Model";
    public SeededSponsorship[] Sponsorships { get; init; } = [new("p1", "Sponsor", "2026-01-10"), new("p2", "Cosponsor", "2026-01-10")];
    public string[] Issues { get; init; } = [];
    public string[] UntrackedIssues { get; init; } = [];
    public int FirstRelease { get; init; } = 1;
    public int? LastRelease { get; init; }
    public string RecordKind { get; init; } = "action";
    public string? BillLine { get; init; }

    public string RecordId { get; init; } = new(Id, 64);
}

internal static class BillsSeed
{
    public static async Task AddBillsAsync(string database, params SeededBill[] bills)
    {
        foreach (var bill in bills) await ViewsSeed.ExecuteAsync(database, Insert(bill));
    }

    private static string EntryRow(SeededBill bill) => bill.BillLine is null ? "" : $"""
        INSERT INTO published_entries (kind, record_id, revision_number, action_record_id, action_revision_number, headline, bill_title, bill_line)
          VALUES ('action', '{bill.RecordId}', 1, '{bill.RecordId}', 1, 'H', 'T', '{bill.BillLine}');
        """;

    private static string Insert(SeededBill bill)
    {
        var last = bill.LastRelease?.ToString(CultureInfo.InvariantCulture) ?? "NULL";
        var cosponsors = bill.Sponsorships.Count(item => item.Role == "Cosponsor");
        return $"""
            SET session_replication_role = replica;
            INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release)
              VALUES ('{bill.RecordKind}', '{bill.RecordId}', {bill.FirstRelease}, 1, {last});
            {PublicActionRows.ActionRevision(bill.RecordId, 1, bill.TextOrigin, bill.EventOn, cosponsors, bill.Congress, bill.BillType, bill.BillNumber, bill.PolicyAreaId)}
            {string.Join('\n', bill.Sponsorships.Select((item, position) => PublicActionRows.ActionSponsorship(
                bill.RecordId, 1, position, item.Person, item.Role, item.IsOriginal, item.SponsoredOn, item.WithdrawnOn)))}
            {string.Join('\n', bill.Issues.Select((issue, position) => PublicActionRows.ActionIssue(bill.RecordId, 1, issue, position, true)))}
            {EntryRow(bill)}
            {string.Join('\n', bill.UntrackedIssues.Select((issue, position) => PublicActionRows.ActionIssue(bill.RecordId, 1, issue, bill.Issues.Length + position, false)))}
            """;
    }
}
