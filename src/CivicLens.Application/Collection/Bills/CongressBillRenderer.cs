using System.Globalization;
using CivicLens.Core.Actions;
using static CivicLens.Application.Collection.RenderedText;

namespace CivicLens.Application.Collection.Bills;

public static class CongressBillRenderer
{
    public const int TextWindowLength = 60_000;
    private const string TimeFormat = "HH:mm:ss";
    internal const string UpdateFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

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

    public static string RenderSubjects(CongressBillListItem bill, CongressBillSubjectsPage page)
    {
        ArgumentNullException.ThrowIfNull(bill);
        ArgumentNullException.ThrowIfNull(page);
        return Page(bill, page.Subjects.Select(subject => Line("Subject", subject)));
    }

    public static string RenderSummaries(CongressBillListItem bill, IReadOnlyList<CongressBillSummaryText> summaries)
    {
        ArgumentNullException.ThrowIfNull(bill);
        ArgumentNullException.ThrowIfNull(summaries);
        var blocks = summaries.Select(SummaryBlock).Prepend(IdentityLine(bill.Congress, bill.Type, bill.Number));
        return string.Join("\n\n", blocks);
    }

    public static string? ReadSummaryBody(string block, string version)
    {
        if (string.IsNullOrEmpty(block) || string.IsNullOrEmpty(version) || block.Contains('\r') || block.Contains("\n\n", StringComparison.Ordinal))
            return null;

        var separator = version.LastIndexOf('@');
        if (separator <= 0 || separator == version.Length - 1) return null;
        var versionCode = version[..separator];
        var updated = version[(separator + 1)..];
        if (versionCode != SingleLine(versionCode) ||
            !DateTimeOffset.TryParseExact(updated, UpdateFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _)) return null;
        var lines = block.Split('\n');
        if (lines.Length < 6 || lines[0] != SummaryVersionLine(versionCode) ||
            !HasActionDate(lines[1]) || !HasHeaderValue(lines[2], "Action") ||
            lines[3] != SummaryUpdatedLine(updated) || lines[4] != SummaryTextLabel)
            return null;

        var body = block[(lines[0].Length + lines[1].Length + lines[2].Length + lines[3].Length + lines[4].Length + 5)..];
        return string.IsNullOrWhiteSpace(body) ? null : body;
    }

    public static CongressBillTextReading RenderText(CongressBillListItem bill, CongressBillTextVersion version, string text)
    {
        ArgumentNullException.ThrowIfNull(bill);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0) throw new InvalidDataException("Bill text is empty.");
        var window = TextWindow(text);
        var lines = new[]
        {
            IdentityLine(bill.Congress, bill.Type, bill.Number),
            Line("Version", version.Type),
            Line("Version date", version.Date is { } date ? Date(BillGate.DayOf(date)) : "not given"),
            Line("Package", version.PackageId),
            Line("Characters read", $"{Count(window.Length)} of {Count(text.Length)}"),
            BillTextBody.Label,
            window
        };
        return new CongressBillTextReading(string.Join('\n', lines), window.Length, text.Length);
    }

    private static string TextWindow(string text)
    {
        if (text.Length <= TextWindowLength) return text;
        for (var index = TextWindowLength; index > 0; index--)
            if (char.IsWhiteSpace(text[index])) return text[..index];
        throw new InvalidDataException("Bill text has no whitespace inside the window.");
    }

    private static string SummaryBlock(CongressBillSummaryText item)
    {
        var summary = item.Summary;
        if (item.Text.Contains("\n\n", StringComparison.Ordinal) || item.Text.Contains('\r'))
            throw new InvalidDataException("Summary text must not hold a blank line or a carriage return.");
        var lines = new List<string>
        {
            SummaryVersionLine(summary),
            Line("Action date", Date(summary.ActionDate)),
            Line("Action", summary.ActionDescription),
            SummaryUpdatedLine(summary),
            SummaryTextLabel
        };
        if (item.Text.Length > 0) lines.Add(item.Text);
        return string.Join('\n', lines);
    }

    internal const string SummaryTextLabel = "Text:";

    internal static string SummaryVersionLine(CongressBillSummary summary) => SummaryVersionLine(summary.VersionCode);

    private static string SummaryVersionLine(string versionCode) => Line("Summary version", versionCode);

    private static bool HasHeaderValue(string line, string label) =>
        line == label + ":" || line.StartsWith(label + ": ", StringComparison.Ordinal) && line.Length > label.Length + 2;

    private static bool HasActionDate(string line) =>
        line.StartsWith("Action date: ", StringComparison.Ordinal) &&
        DateOnly.TryParseExact(line.AsSpan("Action date: ".Length), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    internal static string SummaryUpdatedLine(CongressBillSummary summary) =>
        SummaryUpdatedLine(summary.UpdateDate.UtcDateTime.ToString(UpdateFormat, CultureInfo.InvariantCulture));

    private static string SummaryUpdatedLine(string updated) => Line("Updated", updated);

    internal static string CosponsorLine(CongressBillCosponsor cosponsor)
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

    private static string IdentityLine(int congress, string type, int number) =>
        Line("Bill", string.Join(' ', Count(congress), type, Count(number)));
}
