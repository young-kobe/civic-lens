namespace CivicLens.Application.Analysis;

public sealed record DocumentChangeAnalysisWorkerResult(int Passes, int ProgressCount, int Failures);
