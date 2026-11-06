using CivicLens.Application.Actions;
using CivicLens.Application.Analysis;
using CivicLens.Application.Briefs;
using CivicLens.Application.Relationships;
using CivicLens.Application.Statements;
using CivicLens.Application.Votes;

namespace CivicLens.Application.Spending;

public static class AiSpendingPolicy
{
    private const decimal MaximumCapDollars = 1_000_000m;
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

    public static long ToMicros(decimal capDollars)
    {
        var micros = capDollars * MicrosPerDollar;
        if (capDollars < 0 || capDollars > MaximumCapDollars || micros != decimal.Truncate(micros))
            throw new ArgumentException($"The monthly cap must be 0 to {MaximumCapDollars:0} dollars with at most 6 decimals.", nameof(capDollars));
        return (long)micros;
    }

    public static decimal ToDollars(long micros) => micros / MicrosPerDollar;
}
