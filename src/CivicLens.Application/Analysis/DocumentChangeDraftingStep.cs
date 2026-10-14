using CivicLens.Core.Analysis;
using CivicLens.Core.Review;
using Codes = CivicLens.Application.Analysis.DocumentChangeAnalysisErrorCodes;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Application.Analysis;

public sealed class DocumentChangeDraftingStep(IDocumentChangeAnalysisStore store, IDocumentChangeDraftingModel model,
    DocumentChangeAnalysisSettings settings, DocumentChangeAnalysisCatalog catalog) : IAnalysisTaskStep<AnalysisRun>
{
    private readonly string catalogJson = DocumentChangeDraftingPrompt.SerializeCatalog(catalog);

    public IAnalysisQueue<AnalysisRun> Queue => store;

    public AnalysisSettings Settings => settings;

    public AnalysisTaskIdentity Identity => DocumentChangeDraftingTask.Identity;

    public AnalysisRun Wrap(AnalysisRun run) => run;

    public async Task<bool> RunAsync(IAnalysisAttempt<AnalysisRun> attempt, CancellationToken cancellationToken)
    {
        attempt.ContextJson = catalogJson;
        var evidence = await store.ReadEvidenceAsync(attempt.Claim.Record.SubjectId, cancellationToken);
        if (evidence is null) return await attempt.CompleteAsync(State.Blocked, Codes.ComparisonUnavailable, cancellationToken);
        if (evidence.ExistingDraftId is not null)
            return await attempt.CompleteAsync(State.Succeeded, Codes.HumanDraftExists, cancellationToken);

        var loop = new AnalysisValidationLoop<AnalysisRun, DocumentChangeDraftRevision>(Wrap, attempt, AnalysisRunOutcome.Drafted,
            DocumentChangeDraftingTask.MaximumValidationPasses,
            errors => Bounded(DocumentChangeDraftingPrompt.Build(evidence, catalog, errors)), model.DraftAsync,
            (json, run) => Resolve(json, evidence, run), store.GetRunAsync);
        var result = await loop.RunAsync(cancellationToken);
        return result.End switch
        {
            AnalysisLoopEnd.Accepted => await attempt.SettleAsync(result.Run!,
                DocumentChangeAnalysisCheckpoint.From(attempt.Claim, State.Succeeded, revision: result.Accepted), cancellationToken),
            AnalysisLoopEnd.Exhausted => await attempt.SettleAsync(result.Run!, State.Failed,
                AnalysisErrorCodes.ForOutcome(result.Run!.Outcome), cancellationToken),
            AnalysisLoopEnd.NotSent when result.Run is null => await attempt.CompleteAsync(State.Blocked, Codes.InputLimitExceeded,
                cancellationToken),
            AnalysisLoopEnd.NotSent => await attempt.CompleteAsync(State.Failed, AnalysisErrorCodes.ForOutcome(result.Run!.Outcome),
                cancellationToken),
            _ => result.Progress
        };
    }

    private AnalysisResolution<DocumentChangeDraftRevision> Resolve(string outputJson, DocumentChangeAnalysisEvidence evidence,
        AnalysisRun run)
    {
        var resolution = DocumentChangeDraftingResolver.Resolve(outputJson, evidence, catalog, Guid.NewGuid().ToString("N"),
            run.FinishedAtUtc);
        return resolution.Revision is { } revision ? AnalysisResolution<DocumentChangeDraftRevision>.Accept(revision)
            : AnalysisResolution<DocumentChangeDraftRevision>.Reject(resolution.Errors, resolution.HasCitationErrors);
    }

    private AnalysisModelRequest? Bounded(AnalysisModelRequest? request) =>
        request is not null && request.ReservedTokens <= settings.RunTokenLimit ? request : null;
}
