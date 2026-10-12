using System.Collections.Immutable;
using CivicLens.Application.Analysis;
using CivicLens.Application.Issues;
using CivicLens.Core.Analysis;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Application.Statements;

public sealed class StatementGateStep(IStatementAnalysisStore store, IStatementGateModel model, StatementGateSettings settings,
    IssueConfigurationRevision issueRevision) : IAnalysisTaskStep<StatementGateRun>
{
    private readonly IssueConfiguration issues = issueRevision.ReadConfiguration();

    public IAnalysisQueue<StatementGateRun> Queue => store;

    public AnalysisSettings Settings => settings;

    public AnalysisTaskIdentity Identity => StatementGateTask.Identity;

    public StatementGateRun Wrap(AnalysisRun run) => new(run, issueRevision.Id, null, []);

    public async Task<bool> RunAsync(IAnalysisAttempt<StatementGateRun> attempt, CancellationToken cancellationToken)
    {
        var evidence = await store.ReadEvidenceAsync(attempt.Claim.Record.SubjectId, cancellationToken);
        if (evidence is null)
            return await attempt.CompleteAsync(State.Blocked, StatementGateErrorCodes.EvidenceUnavailable, cancellationToken);
        var rubric = StatementGateRubric.Create(issueRevision.Id, issues, evidence.FirstCapturedOn);
        attempt.ContextJson = rubric.ToContextJson();
        if (rubric.Issues.IsEmpty) return await BypassAsync(attempt, evidence, StatementGateReasons.NoTrackedIssue, null, cancellationToken);

        var number = 1;
        string? previousRunId = null;
        ImmutableArray<string> errors = default;
        if (attempt.Claim.Record.RetryOfRunId is { } retryOf)
        {
            var previous = await store.GetRunAsync(retryOf, cancellationToken)
                ?? throw new InvalidDataException("The rejected run that a retry continues is missing.");
            (number, previousRunId, errors) = (2, previous.Run.RunId, previous.Run.ValidationErrors);
        }
        var request = StatementGatePrompt.Build(evidence, rubric, settings.PromptByteCap, errors);
        if (request is null)
            return await BypassAsync(attempt, evidence, number == 1 ? StatementGateReasons.OverByteCap : StatementGateReasons.FailedOpen,
                previousRunId, cancellationToken);

        while (true)
        {
            var call = await attempt.CallModelAsync(request, previousRunId, model.ClassifyAsync, cancellationToken);
            if (call.Response is not { } response) return call.Progress;
            var runId = attempt.RunId!;
            var outcome = response.StopOutcome(AnalysisRunOutcome.Classified);
            var run = Wrap(attempt.ResponseRun(outcome, response));
            if (outcome != AnalysisRunOutcome.Classified)
                return await attempt.SettleAsync(run, State.Failed, AnalysisErrorCodes.ForOutcome(outcome), cancellationToken);

            var resolution = StatementGateResolver.Resolve(response.OutputJson!, evidence, rubric);
            if (resolution.IsValid)
                return await attempt.SettleAsync(run with { Reason = resolution.Reason, Quotes = resolution.Quotes }, State.Succeeded,
                    resolution.Reason, cancellationToken);

            var rejected = run with
            {
                Run = run.Run with
                {
                    Outcome = resolution.HasCitationErrors ? AnalysisRunOutcome.CitationRejected : AnalysisRunOutcome.OutputRejected,
                    ValidationErrors = resolution.Errors
                }
            };
            var retry = number < StatementGateTask.MaximumValidationPasses
                ? StatementGatePrompt.Build(evidence, rubric, settings.PromptByteCap, resolution.Errors) : null;
            if (retry is null)
                return await attempt.SettleAsync(rejected with { Reason = StatementGateReasons.FailedOpen }, State.Succeeded,
                    StatementGateReasons.FailedOpen, cancellationToken);
            if (!await attempt.SettleAsync(rejected, State.Running, null, cancellationToken)) return false;
            (number, previousRunId, request) = (number + 1, runId, retry);
        }
    }

    private Task<bool> BypassAsync(IAnalysisAttempt<StatementGateRun> attempt, StatementGateEvidence evidence, string reason,
        string? previousRunId, CancellationToken cancellationToken)
    {
        var run = attempt.BypassRun(StatementGateTask.Model, evidence.Extraction.TextSha256, previousRunId);
        return attempt.SettleAsync(new StatementGateRun(run, issueRevision.Id, reason, []), State.Succeeded, reason, cancellationToken);
    }
}
