using System.Collections.Immutable;
using CivicLens.Application.Analysis;
using CivicLens.Application.Issues;
using CivicLens.Core.Analysis;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Application.Statements;

public sealed class StatementGateStep(IStatementGateStore store, IStatementGateModel model, StatementGateSettings settings,
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

        var loop = new AnalysisValidationLoop<StatementGateRun, StatementGateResolution>(Wrap, attempt, AnalysisRunOutcome.Classified,
            StatementGateTask.MaximumValidationPasses, errors => StatementGatePrompt.Build(evidence, rubric, settings.PromptByteCap, errors),
            model.ClassifyAsync, (json, _) => Resolve(json, evidence, rubric), async (runId, token) => (await store.GetRunAsync(runId, token))?.Run);
        var result = await loop.RunAsync(cancellationToken);
        return result.End switch
        {
            AnalysisLoopEnd.Accepted => await attempt.SettleAsync(Wrap(result.Run!) with
            {
                Reason = result.Accepted!.Reason,
                Quotes = result.Accepted!.Quotes
            }, State.Succeeded, result.Accepted!.Reason, cancellationToken),
            AnalysisLoopEnd.Exhausted => await attempt.SettleAsync(Wrap(result.Run!) with { Reason = StatementGateReasons.FailedOpen },
                State.Succeeded, StatementGateReasons.FailedOpen, cancellationToken),
            AnalysisLoopEnd.NotSent => await BypassAsync(attempt, evidence,
                result.Run is null ? StatementGateReasons.OverByteCap : StatementGateReasons.FailedOpen, result.Run?.RunId, cancellationToken),
            _ => result.Progress
        };
    }

    private static AnalysisResolution<StatementGateResolution> Resolve(string outputJson, StatementGateEvidence evidence,
        StatementGateRubric rubric)
    {
        var resolution = StatementGateResolver.Resolve(outputJson, evidence, rubric);
        return resolution.IsValid ? AnalysisResolution<StatementGateResolution>.Accept(resolution)
            : AnalysisResolution<StatementGateResolution>.Reject(resolution.Errors, resolution.HasCitationErrors);
    }

    private Task<bool> BypassAsync(IAnalysisAttempt<StatementGateRun> attempt, StatementGateEvidence evidence, string reason,
        string? previousRunId, CancellationToken cancellationToken)
    {
        var run = attempt.BypassRun(StatementGateTask.Model, evidence.Extraction.TextSha256, previousRunId);
        return attempt.SettleAsync(new StatementGateRun(run, issueRevision.Id, reason, []), State.Succeeded, reason, cancellationToken);
    }
}
