using CivicLens.Application.Analysis;

namespace CivicLens.Application.Statements;

public sealed record StatementAnalysisStatusReport<TView>(AnalysisStatus? Status, long ChargedToday, IReadOnlyDictionary<TView, int> Counts)
    where TView : struct, Enum;
