using System.Globalization;
using System.Text.RegularExpressions;
using CivicLens.Application.Actions;
using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Votes;
using CivicLens.Core.Relationships;
using CivicLens.Core.Review;
using CivicLens.Core.Votes;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Review;

public sealed partial class LookupDraft(IActionReviewStore actions, IVoteReviewStore votes, TimeProvider clock)
{
    public async Task<DraftLookupHit?> ExecuteAsync(ReviewActor actor, string? query, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        var text = query?.Trim() ?? "";
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var congress = CongressCalendar.CongressOn(today) ?? throw new InvalidOperationException("The clock date has no Congress.");
        if (RollPattern().Match(text) is { Success: true } roll)
            return await FindVoteAsync(roll.Groups["senate"].Success ? Chamber.Senate : Chamber.House, congress, CongressCalendar.SessionOn(today), Number(roll), cancellationToken);
        if (BillPattern().Match(text) is not { Success: true } bill) throw new DraftLookupFormatException();
        var billCongress = bill.Groups["congress"].Success
            ? int.Parse(bill.Groups["congress"].Value, CultureInfo.InvariantCulture) : congress;
        var type = CongressBillListItem.NormalizeCitationType(Regex.Replace(bill.Groups["type"].Value, @"\s", ""));
        if (billCongress < 1 || type is null) throw new DraftLookupFormatException();
        return await FindActionAsync(billCongress, type, Number(bill), cancellationToken);
    }

    private async Task<DraftLookupHit?> FindActionAsync(int congress, string billType, int billNumber, CancellationToken cancellationToken) =>
        await actions.FindRecordIdByBillAsync(congress, billType, billNumber, cancellationToken) is { } recordId
            ? new(PublishedRecordKind.Action, recordId) : null;

    private async Task<DraftLookupHit?> FindVoteAsync(Chamber chamber, int congress, int session, int roll,
        CancellationToken cancellationToken) =>
        await votes.FindRecordIdByRollAsync(chamber, congress, session, roll, cancellationToken) is { } recordId
            ? new(PublishedRecordKind.Vote, recordId) : null;

    private static int Number(Match match) => int.Parse(match.Groups["number"].Value, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^(?<senate>senate\s+)?roll\s*(?<number>[1-9]\d{0,4})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RollPattern();

    [GeneratedRegex(@"^(?:(?<congress>\d{1,3})\s+)?(?<type>[A-Za-z][A-Za-z. ]*?)\s*(?<number>[1-9]\d{0,4})$", RegexOptions.CultureInvariant)]
    private static partial Regex BillPattern();
}
