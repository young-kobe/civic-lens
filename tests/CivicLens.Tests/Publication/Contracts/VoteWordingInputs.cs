using System.Text;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Publication.Contracts;

/// <summary>Every input of <see cref="NextStep"/> and every output of it and of <see cref="VoteMeaning"/>. The owner approves the public wording from the table this class writes.</summary>
internal static class VoteWordingInputs
{
    public const string TablePathVariable = "CIVIC_LENS_WORDING_TABLE";

    public static readonly string[] Results = ["Passed", "Agreed to", "Confirmed", "Failed", "Not Agreed to", "Not Confirmed"];
    public static readonly string?[] BillTypes = [null, "hr", "s", "hjres", "sjres", "hconres", "sconres", "hres", "sres"];

    public static IEnumerable<(PublishedVoteKind Kind, string Result, PublishedChamber Chamber, string? BillType, string Text)> NextSteps() =>
        from kind in Enum.GetValues<PublishedVoteKind>()
        from result in Results
        from chamber in Enum.GetValues<PublishedChamber>()
        from billType in BillTypes
        let text = NextStep.Of(kind, result, chamber, billType)
        where text is not null
        select (kind, result, chamber, billType, text);

    public static string Table()
    {
        var table = new StringBuilder();
        table.AppendLine("### What a Yes vote and a No vote meant").AppendLine();
        table.AppendLine("| Kind | Yes | No |").AppendLine("|---|---|---|");
        foreach (var kind in Enum.GetValues<PublishedVoteKind>())
            if (VoteMeaning.Of(kind) is { } meaning) table.AppendLine($"| {kind} | {meaning.Yes} | {meaning.No} |");
        table.AppendLine().AppendLine("### What happens next").AppendLine();
        table.AppendLine("Result: Passed covers Passed, Agreed to and Confirmed. Failed covers Failed, Not Agreed to and Not Confirmed. A bill type lists the vote chamber and the bill type codes.").AppendLine();
        table.AppendLine("| Kind | Result | When | Sentences |").AppendLine("|---|---|---|---|");
        var steps = NextSteps().ToArray();
        foreach (var kind in steps.Select(step => step.Kind).Distinct())
            foreach (var group in steps.Where(step => step.Kind == kind).GroupBy(step => (Passed: IsPassed(step.Result), step.Text)).OrderByDescending(group => group.Key.Passed))
                table.AppendLine($"| {kind} | {(group.Key.Passed ? "Passed" : "Failed")} | {When(group)} | {group.Key.Text} |");
        table.AppendLine().AppendLine($"No next step: `{NextStep.NotShown}`");
        return table.ToString();
    }

    private static bool IsPassed(string result) => result is "Passed" or "Agreed to" or "Confirmed";

    private static string When(IEnumerable<(PublishedVoteKind Kind, string Result, PublishedChamber Chamber, string? BillType, string Text)> group) =>
        string.Join("; ", group.GroupBy(step => step.Chamber).Select(chamber =>
        {
            var types = chamber.Select(step => step.BillType ?? "none").Distinct().ToArray();
            return $"{chamber.Key}, {(types.Length == BillTypes.Length ? "any bill type" : string.Join(" ", types))}";
        }));
}
