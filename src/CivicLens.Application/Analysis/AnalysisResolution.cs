using System.Collections.Immutable;

namespace CivicLens.Application.Analysis;

public readonly record struct AnalysisResolution<TAccepted>(TAccepted? Accepted, ImmutableArray<string> Errors, bool HasCitationErrors)
{
    public bool IsAccepted => Errors.IsDefaultOrEmpty;

    public static AnalysisResolution<TAccepted> Accept(TAccepted accepted) => new(accepted, [], false);

    public static AnalysisResolution<TAccepted> Reject(ImmutableArray<string> errors, bool hasCitationErrors) =>
        new(default, errors, hasCitationErrors);
}
