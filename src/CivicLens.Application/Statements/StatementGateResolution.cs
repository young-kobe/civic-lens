using System.Collections.Immutable;

namespace CivicLens.Application.Statements;

public sealed record StatementGateResolution(string? Reason, ImmutableArray<StatementGateQuote> Quotes, ImmutableArray<string> Errors,
    bool HasCitationErrors)
{
    public bool IsValid => Reason is not null;
}
