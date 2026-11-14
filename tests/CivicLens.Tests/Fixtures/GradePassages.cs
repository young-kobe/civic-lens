namespace CivicLens.Tests.Fixtures;

/// <summary>A headline and summary pair on each side of the AI grade limit of 14, chosen with the real grader. The summary is the same, so only
/// the headline moves the grade. The hardest sentence is the first sentence of the summary.</summary>
internal static class GradePassages
{
    public const string Summary = "The Secretary administers supplementary rental assistance for eligible beneficiaries each year. Local agencies administer the program.";
    public const string HardestSentence = "The Secretary administers supplementary rental assistance for eligible beneficiaries each year.";

    /// <summary>Reads at grade 13.5 with <see cref="Summary"/>: above the target of 12, so it passes only because code rejects above 14.</summary>
    public const string PassingHeadline = "The bill renews rent aid.";

    /// <summary>Reads at grade 14.1 with <see cref="Summary"/>.</summary>
    public const string FailingHeadline = "The bill renews housing aid.";

    public const string ApprovalMessage = "The text reads at grade 14.1 as one passage. The limit is grade 14.0.";

    public const string RepairMessageStart = "The text reads at grade 14.1 as one passage. Write at grade 12 or below. The hardest sentence is \"The Secretary administers";
}
