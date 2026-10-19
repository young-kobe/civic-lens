using CivicLens.Core.Analysis;
using CivicLens.Core.Review;
using Codes = CivicLens.Application.Analysis.DocumentChangeDraftErrorCodes;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Application.Analysis;

public sealed class DocumentChangeDraftStep(IDocumentChangeAnalysisStore store, IDocumentChangeDraftModel model,
    DocumentChangeDraftSettings settings, DocumentChangeDraftCatalog catalog) : IAnalysisTaskStep<AnalysisRun>
{
    private readonly string catalogJson = DocumentChangeDraftPrompt.SerializeCatalog(catalog);

    public IAnalysisQueue<AnalysisRun> Queue => store;

    public AnalysisSettings Settings => settings;

    public AnalysisTaskIdentity Identity => DocumentChangeDraftTask.Identity;

    public AnalysisRun Wrap(AnalysisRun run) => run;

    public async Task<bool> RunAsync(IAnalysisAttempt<AnalysisRun> attempt, CancellationToken cancellationToken)
    {
        attempt.ContextJson = catalogJson;
        var evidence = await store.ReadEvidenceAsync(attempt.Claim.Record.SubjectId, cancellationToken);
        if (evidence is null) return await attempt.CompleteAsync(State.Blocked, Codes.ComparisonUnavailable, cancellationToken);
        if (evidence.ExistingDraftId is not null)
            return await attempt.CompleteAsync(State.Succeeded, Codes.HumanDraftExists, cancellationToken);

        var loop = new AnalysisValidationLoop<AnalysisRun, DocumentChangeDraftRevision>(Wrap, attempt, AnalysisRunOutcome.Drafted,
            DocumentChangeDraftTask.MaximumValidationPasses,
            errors => Bounded(DocumentChangeDraftPrompt.Build(evidence, catalog, errors)), model.DraftAsync,
            (json, run) => Resolve(json, evidence, run), store.GetRunAsync);
        var result = await loop.RunAsync(cancellationToken);
        return result.End switch
        {
            AnalysisLoopEnd.Accepted => await attempt.SettleAsync(result.Run!,
                DocumentChangeDraftCheckpoint.From(attempt.Claim, result.Accepted!), cancellationToken),
            AnalysisLoopEnd.NotSent when result.Run is null => await attempt.CompleteAsync(State.Blocked, Codes.InputLimitExceeded,
                cancellationToken),
            AnalysisLoopEnd.Exhausted or AnalysisLoopEnd.NotSent => await loop.FailAsync(result, cancellationToken),
            _ => result.Progress
        };
    }

    private AnalysisResolution<DocumentChangeDraftRevision> Resolve(string outputJson, DocumentChangeDraftEvidence evidence,
        AnalysisRun run)
    {
        var resolution = DocumentChangeDraftResolver.Resolve(outputJson, evidence, catalog, Guid.NewGuid().ToString("N"),
            run.FinishedAtUtc);
        return resolution.Revision is { } revision ? AnalysisResolution<DocumentChangeDraftRevision>.Accept(revision)
            : AnalysisResolution<DocumentChangeDraftRevision>.Reject(resolution.Errors, resolution.HasCitationErrors);
    }

    private AnalysisModelRequest? Bounded(AnalysisModelRequest? request) =>
        request is not null && request.ReservedTokens <= settings.CallTokenLimit ? request : null;
}
