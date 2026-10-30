using System.Collections.Immutable;
using System.Text.Json;

namespace CivicLens.Application.Statements;

public sealed record StatementGateCarryOver(ImmutableArray<string> IssueIds, ImmutableArray<string> PolicyAreaIds, bool GatePassed)
{
    public static readonly StatementGateCarryOver None = new([], [], GatePassed: false);

    public static StatementGateCarryOver From(StatementGateRun? gateRun)
    {
        if (gateRun is not { Run.OutputJson: { } outputJson } || !StatementGateReasons.IsPass(gateRun.Reason)) return None;
        var output = StatementGateResolver.Parse(outputJson)
            ?? throw new InvalidDataException("The stored output of a passed gate run is not valid.");
        return new([.. gateRun.Quotes.Select(quote => quote.IssueId)], [.. output.PolicyAreas.Select(area => area.Id)],
            GatePassed: true);
    }
}
