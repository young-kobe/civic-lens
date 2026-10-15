using CivicLens.Application.Statements;
using CivicLens.Tests.Application.Analysis;

namespace CivicLens.Tests.Fixtures;

internal static class StatementAiDrafting
{
    public static Task PassGateAsync(IStatementAnalysisStore gates) => GateAsync(gates,
        StatementGateFixtures.Output([StatementGateFixtures.HousingArea], 0.8, (StatementGateFixtures.RentIssue, "rent cap")));

    public static Task SkipGateAsync(IStatementAnalysisStore gates) => GateAsync(gates, StatementGateFixtures.Output());

    public static Task DraftAsync(IStatementDraftStore drafts, string output) =>
        AnalysisWorkers.StatementDrafting(drafts, new FakeAnalysisStatusStore(), new FakeStatementDraftModel().Draft(output),
            new StatementDraftSettings(10_000_000, concurrency: 1), TimeProvider.System).ExecuteAsync(once: true);

    private static Task GateAsync(IStatementAnalysisStore gates, string output) =>
        AnalysisWorkers.Gate(gates, new FakeAnalysisStatusStore(), new FakeStatementGateModel().Classify(output),
            new StatementGateSettings(10_000_000, concurrency: 1), StatementGateFixtures.Revision(), TimeProvider.System).ExecuteAsync(once: true);
}
