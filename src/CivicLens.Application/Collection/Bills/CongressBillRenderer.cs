using System.Globalization;
using System.Text.RegularExpressions;

namespace CivicLens.Application.Collection.Bills;

public static partial class CongressBillRenderer
{
    public const string NormalizationVersion = "congress-bill-render-v1";
    private const string DateFormat = "yyyy-MM-dd";
    private const string TimeFormat = "HH:mm:ss";
    private const string UpdateFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    public static string RenderDetail(CongressBill bill)
    {
        ArgumentNullException.ThrowIfNull(bill);
        var lines = new List<string>
        {
            IdentityLine(bill.Congress, bill.Type, bill.Number),
            Line("Title", bill.Title),
            Line("Introduced", Date(bill.IntroducedDate)),
            Line("Updated", bill.UpdateDate.UtcDateTime.ToString(UpdateFormat, CultureInfo.InvariantCulture))
        };
        if (bill.PolicyArea is not null) lines.Add(Line("Policy area", bill.PolicyArea));
        lines.AddRange(bill.SponsorBioguideIds.Select(id => Line("Sponsor", id)));
        lines.Add(Line("Cosponsors (including withdrawn)", Count(bill.CosponsorCount)));
        lines.Add(Line("Actions", Count(bill.ActionCount)));
        return string.Join('\n', lines);
    }

    public static string RenderCosponsors(CongressBillListItem bill, CongressBillCosponsorsPage page)
    {
        ArgumentNullException.ThrowIfNull(bill);
        ArgumentNullException.ThrowIfNull(page);
        return Page(bill, page.Cosponsors.Select(CosponsorLine));
    }

    public static string RenderActions(CongressBillListItem bill, CongressBillActionsPage page)
    {
        ArgumentNullException.ThrowIfNull(bill);
        ArgumentNullException.ThrowIfNull(page);
        return Page(bill, page.Actions.Select(ActionLine));
    }

    public static string RenderSummaries(CongressBillListItem bill, IReadOnlyList<CongressBillSummaryText> summaries)
    {
        ArgumentNullException.ThrowIfNull(bill);
        ArgumentNullException.ThrowIfNull(summaries);
        var blocks = summaries.Select(SummaryBlock).Prepend(IdentityLine(bill.Congress, bill.Type, bill.Number));
        return string.Join("\n\n", blocks);
    }

    private static string SummaryBlock(CongressBillSummaryText item)
    {
        var summary = item.Summary;
        if (item.Text.Contains("\n\n", StringComparison.Ordinal) || item.Text.Contains('\r'))
            throw new InvalidDataException("Summary text must not hold a blank line or a carriage return.");
        var lines = new List<string>
        {
            Line("Summary version", summary.VersionCode),
            Line("Action date", Date(summary.ActionDate)),
            Line("Action", summary.ActionDescription),
            Line("Updated", summary.UpdateDate.UtcDateTime.ToString(UpdateFormat, CultureInfo.InvariantCulture)),
            "Text:"
        };
        if (item.Text.Length > 0) lines.Add(item.Text);
        return string.Join('\n', lines);
    }

    private static string CosponsorLine(CongressBillCosponsor cosponsor)
    {
        var fields = new List<string> { cosponsor.BioguideId };
        if (cosponsor.IsOriginalCosponsor) fields.Add("original");
        fields.Add(Date(cosponsor.SponsorshipDate));
        if (cosponsor.SponsorshipWithdrawnDate is { } withdrawn)
            fields.Add("withdrawn " + Date(withdrawn));
        return Line("Cosponsor", string.Join(", ", fields));
    }

    private static string ActionLine(CongressBillAction action)
    {
        var when = Date(action.ActionDate);
        if (action.ActionTime is { } time) when += " " + time.ToString(TimeFormat, CultureInfo.InvariantCulture);
        var tags = new[]
        {
            Tag("type", action.Type),
            Tag("code", action.ActionCode),
            Tag("source", action.SourceSystemCode is { } code ? Count(code) : null, action.SourceSystemName)
        };
        return Line($"Action {when}{string.Concat(tags)}", action.Text);
    }

    private static string Tag(string name, params string?[] values)
    {
        var present = values.Where(value => value is not null).Select(value => SingleLine(value!)).ToArray();
        return present.Length == 0 ? string.Empty : $" [{name} {string.Join(' ', present)}]";
    }

    private static string Page(CongressBillListItem bill, IEnumerable<string> factLines) =>
        string.Join('\n', factLines.Prepend(IdentityLine(bill.Congress, bill.Type, bill.Number)));

    private static string Date(DateOnly date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);

    private static string IdentityLine(int congress, string type, int number) =>
        Line("Bill", string.Join(' ', Count(congress), type, Count(number)));

    private static string Line(string label, string value) => $"{label}: {SingleLine(value)}".TrimEnd();

    private static string SingleLine(string value) => LineBreaks().Replace(value, " ").Trim();

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

    [GeneratedRegex(@"\r\n|[\r\n\v\f\u0085\u2028\u2029]")]
    private static partial Regex LineBreaks();
}
