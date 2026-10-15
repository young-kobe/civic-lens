using CivicLens.Core.Analysis;

namespace CivicLens.Application.Analysis;

public enum AnalysisLoopEnd { Settled, Accepted, Exhausted, NotSent }

public readonly record struct AnalysisLoopResult<TAccepted>(AnalysisLoopEnd End, bool Progress, AnalysisRun? Run, TAccepted? Accepted)
{
    public static AnalysisLoopResult<TAccepted> Settled(bool progress) => new(AnalysisLoopEnd.Settled, progress, null, default);

    public static AnalysisLoopResult<TAccepted> AcceptedBy(AnalysisRun run, TAccepted accepted) =>
        new(AnalysisLoopEnd.Accepted, true, run, accepted);

    public static AnalysisLoopResult<TAccepted> ExhaustedAfter(AnalysisRun rejected) => new(AnalysisLoopEnd.Exhausted, true, rejected, default);

    public static AnalysisLoopResult<TAccepted> NotSent(AnalysisRun? resumedFrom) => new(AnalysisLoopEnd.NotSent, true, resumedFrom, default);
}
