using CivicLens.Core.Analysis;

namespace CivicLens.Application.Analysis;

public sealed record AnalysisModelResponse(string StopReason, string? OutputJson, AnalysisTokenUsage Usage, string? RequestId)
{
    public AnalysisRunOutcome StopOutcome(AnalysisRunOutcome completed) => StopReason switch
    {
        "refusal" => AnalysisRunOutcome.Refused,
        "max_tokens" => AnalysisRunOutcome.OutputLimitReached,
        "end_turn" when OutputJson is not null => completed,
        _ => AnalysisRunOutcome.UnexpectedStop
    };
}
