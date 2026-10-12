using System.Collections.Immutable;
using CivicLens.Core.Analysis;
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

        var number = 1;
        string? previousRunId = null;
        ImmutableArray<string> errors = default;
        var rejectedCode = Codes.InputLimitExceeded;
        if (attempt.Claim.Record.RetryOfRunId is { } retryOf)
        {
            var previous = await store.GetRunAsync(retryOf, cancellationToken)
                ?? throw new InvalidDataException("The rejected run that a retry continues is missing.");
            (number, previousRunId, errors, rejectedCode) = (2, previous.RunId, previous.ValidationErrors, AnalysisErrorCodes.ForOutcome(previous.Outcome));
        }
        var request = Bounded(DocumentChangeDraftingPrompt.Build(evidence, catalog, errors));
        if (request is null)
            return await attempt.CompleteAsync(number == 1 ? State.Blocked : State.Failed, rejectedCode, cancellationToken);

        while (true)
        {
            var call = await attempt.CallModelAsync(request, previousRunId, model.DraftAsync, cancellationToken);
            if (call.Response is not { } response) return call.Progress;
            var runId = attempt.RunId!;
            var outcome = response.StopOutcome(AnalysisRunOutcome.Drafted);
            var run = attempt.ResponseRun(outcome, response);
            if (outcome != AnalysisRunOutcome.Drafted)
                return await attempt.SettleAsync(run, State.Failed, AnalysisErrorCodes.ForOutcome(outcome), cancellationToken);

            var resolution = DocumentChangeDraftingResolver.Resolve(response.OutputJson!, evidence, catalog,
                Guid.NewGuid().ToString("N"), run.FinishedAtUtc);
            if (resolution.Revision is not null)
                return await attempt.SettleAsync(run,
                    DocumentChangeAnalysisCheckpoint.From(attempt.Claim, State.Succeeded, revision: resolution.Revision), cancellationToken);
            var rejected = run with
            {
                Outcome = resolution.HasCitationErrors ? AnalysisRunOutcome.CitationRejected : AnalysisRunOutcome.OutputRejected,
                ValidationErrors = resolution.Errors
            };
            var retry = number < DocumentChangeDraftingTask.MaximumValidationPasses
                ? Bounded(DocumentChangeDraftingPrompt.Build(evidence, catalog, resolution.Errors)) : null;
            if (retry is null)
                return await attempt.SettleAsync(rejected, State.Failed, AnalysisErrorCodes.ForOutcome(rejected.Outcome), cancellationToken);
            if (!await attempt.SettleAsync(rejected, State.Running, null, cancellationToken)) return false;
            (number, previousRunId, request) = (number + 1, runId, retry);
        }
    }

    private AnalysisModelRequest? Bounded(AnalysisModelRequest? request) =>
        request is not null && request.ReservedTokens <= settings.RunTokenLimit ? request : null;
}
