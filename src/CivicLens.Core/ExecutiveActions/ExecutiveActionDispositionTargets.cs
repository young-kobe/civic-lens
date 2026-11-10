using System.Text.RegularExpressions;

namespace CivicLens.Core.ExecutiveActions;

/// <summary>
/// Owns the rule that turns verbatim disposition notes into typed targets. A line must start with "Amends:" or "Revokes:".
/// A semicolon splits the line into references. A reference is a target only when it is exactly "EO n" or "Proc. n",
/// with an optional ", Month d, yyyy" date. Any other reference stays in the note only. The rule never guesses.
/// </summary>
public static partial class ExecutiveActionDispositionTargets
{
    public const int MaximumTargets = 100;

    private static readonly (string Prefix, ExecutiveActionRelation Relation)[] Relations =
        [("Amends:", ExecutiveActionRelation.Amends), ("Revokes:", ExecutiveActionRelation.Revokes)];

    public static IReadOnlyList<ExecutiveActionDispositionTarget> Parse(string notes)
    {
        var targets = new List<ExecutiveActionDispositionTarget>();
        var lineStart = 0;
        foreach (var line in notes.Split('\n'))
        {
            AddLineTargets(targets, line, lineStart);
            lineStart += line.Length + 1;
        }
        return targets.Count > MaximumTargets ? [] : targets;
    }

    private static void AddLineTargets(List<ExecutiveActionDispositionTarget> targets, string line, int lineStart)
    {
        foreach (var (prefix, relation) in Relations)
        {
            if (!line.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var referenceStart = prefix.Length;
            foreach (var part in line[prefix.Length..].Split(';'))
            {
                AddReference(targets, relation, part, lineStart + referenceStart);
                referenceStart += part.Length + 1;
            }
            return;
        }
    }

    private static void AddReference(List<ExecutiveActionDispositionTarget> targets, ExecutiveActionRelation relation, string part, int partStart)
    {
        var reference = part.Trim();
        if (Reference().Match(reference) is not { Success: true } match) return;
        var kind = match.Groups["kind"].Value == "EO" ? ExecutiveActionKind.ExecutiveOrder : ExecutiveActionKind.Proclamation;
        var number = match.Groups["number"].Value;
        if (targets.Exists(target => target.Relation == relation && target.Kind == kind && target.Number == number)) return;
        targets.Add(new(relation, kind, number, partStart + part.IndexOf(reference, StringComparison.Ordinal), reference.Length));
    }

    [GeneratedRegex(@"^(?<kind>EO|Proc\.) (?<number>[1-9][0-9]{0,5})(, (January|February|March|April|May|June|July|August|September|October|November|December) [1-9][0-9]?, [0-9]{4})?$",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex Reference();
}
