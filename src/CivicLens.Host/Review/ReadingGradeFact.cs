using System.Globalization;
using CivicLens.Application.Analysis;
using CivicLens.PublicWeb.Components.Evidence;

namespace CivicLens.Host.Review;

/// <summary>The one owner of the draft fact row that shows the passage grade of the text on a page where a person approves. The grade is advice
/// only. <see cref="AiTextRules"/> owns the grade and the target, and no approval check reads this row.</summary>
internal static class ReadingGradeFact
{
    public const string Label = "Reading grade";

    private static readonly string Target = AiTextRules.TargetGrade.ToString("0", CultureInfo.InvariantCulture);

    public static Fact Of(IEnumerable<string?> pieces) =>
        new(Label, $"{ReadingGrade.Format(AiTextRules.PassageGrade(pieces))}. Prompts aim for {Target}. Code does not check this text.");
}
