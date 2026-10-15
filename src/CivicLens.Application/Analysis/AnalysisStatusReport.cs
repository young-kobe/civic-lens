namespace CivicLens.Application.Analysis;

public sealed record AnalysisStatusReport<TView>(AnalysisStatus? Status, long ChargedToday, IReadOnlyDictionary<TView, int> Counts)
    where TView : struct, Enum;
