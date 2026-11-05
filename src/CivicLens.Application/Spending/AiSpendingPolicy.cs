using CivicLens.Application.Actions;
using CivicLens.Application.Analysis;
using CivicLens.Application.Briefs;
using CivicLens.Application.Relationships;
using CivicLens.Application.Statements;
using CivicLens.Application.Votes;

namespace CivicLens.Application.Spending;

public static class AiSpendingPolicy
{
    public const decimal MaximumCapDollars = 1_000_000m;
    private const decimal MicrosPerDollar = 1_000_000m;

    public static readonly IReadOnlyList<string> ModelsInUse =
    [
        .. new[]
        {
            DocumentChangeDraftTask.Model, StatementGateTask.Model, StatementDraftTask.Model, ActionDraftTask.Model,
            VoteDraftTask.Model, RelationshipDraftTask.Model, BriefDraftTask.Model, BriefCheckTask.Model
        }.Distinct().Order(StringComparer.Ordinal)
    ];

    public static DateOnly MonthOf(DateOnly day) => new(day.Year, day.Month, 1);

    public static long ToMicros(decimal dollars, decimal maximumDollars, string name)
    {
        var micros = dollars * MicrosPerDollar;
        if (dollars < 0 || dollars > maximumDollars || micros != decimal.Truncate(micros))
            throw new ArgumentException($"The {name} must be 0 to {maximumDollars:0} dollars with at most 6 decimals.", nameof(dollars));
        return (long)micros;
    }

    public static decimal ToDollars(long micros) => micros / MicrosPerDollar;
}
