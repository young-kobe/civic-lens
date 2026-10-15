using CivicLens.Application;
using CivicLens.Application.Actions;
using CivicLens.Application.Analysis;
using CivicLens.Application.Issues;
using CivicLens.Application.Statements;
using CivicLens.Core.Analysis;

namespace CivicLens.Tests.Fixtures;

internal static class AnalysisWorkers
{
    public static AnalysisWorker<AnalysisRun> Drafting(IDocumentChangeAnalysisStore store, IAnalysisStatusStore status,
        IDocumentChangeDraftModel model, DocumentChangeDraftSettings settings, DocumentChangeAnalysisCatalog catalog,
        TimeProvider clock, IWorkerWakeup? wakeup = null) =>
        new(new DocumentChangeDraftStep(store, model, settings, catalog), status, clock, wakeup);

    public static AnalysisWorker<StatementGateRun> Gate(IStatementAnalysisStore store, IAnalysisStatusStore status,
        IStatementGateModel model, StatementGateSettings settings, IssueConfigurationRevision revision, TimeProvider clock) =>
        new(new StatementGateStep(store, model, settings, revision), status, clock);

    public static AnalysisWorker<StatementDraftRun> StatementDrafting(IStatementDraftStore store, IAnalysisStatusStore status,
        IStatementDraftModel model, StatementDraftSettings settings, TimeProvider clock) =>
        new(new StatementDraftStep(store, model, settings), status, clock);

    public static AnalysisWorker<ActionDraftRun> ActionDrafting(IActionDraftStore store, IAnalysisStatusStore status, ActionBillReader reader,
        IActionDraftModel model, ActionDraftSettings settings, IssueConfigurationRevision issues, TimeProvider clock) =>
        new(new ActionDraftStep(store, reader, model, settings, issues), status, clock);
}
