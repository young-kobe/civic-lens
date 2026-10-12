using CivicLens.Core.Analysis;

namespace CivicLens.Application.Analysis;

public readonly record struct AnalysisCall(AnalysisModelResponse? Response, bool Progress);

public interface IAnalysisAttempt<TRun> where TRun : class
{
    AnalysisClaim Claim { get; }
    string? ContextJson { get; set; }
    string? RunId { get; }
    Task<AnalysisCall> CallModelAsync(AnalysisModelRequest request, string? previousRunId,
        Func<AnalysisModelRequest, CancellationToken, Task<AnalysisModelResponse>> send, CancellationToken cancellationToken);
    AnalysisRun ResponseRun(AnalysisRunOutcome outcome, AnalysisModelResponse response);
    AnalysisRun BypassRun(string model, string inputHash, string? previousRunId);
    Task<bool> SettleAsync(TRun run, AnalysisCheckpoint checkpoint, CancellationToken cancellationToken);
    Task<bool> SettleAsync(TRun run, AnalysisState state, string? errorCode, CancellationToken cancellationToken);
    Task<bool> CompleteAsync(AnalysisState state, string errorCode, CancellationToken cancellationToken);
}
