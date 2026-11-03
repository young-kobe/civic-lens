using CivicLens.Application.Analysis;

namespace CivicLens.Application.Briefs;

public static class BriefCheckLifecycle
{
    private static readonly AnalysisLifecycle Rules = new(AcceptsSuccess, _ => true, _ => true);

    public static bool CanTransition(AnalysisRecord record, AnalysisCheckpoint update) => Rules.CanTransition(record, update);

    private static bool AcceptsSuccess(AnalysisCheckpoint update) =>
        update.ErrorCode is null or BriefDraftErrorCodes.Unchanged;
}
