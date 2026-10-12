using CivicLens.Application.Statements;
using static CivicLens.Tests.Fixtures.StatementGateFixtures;

namespace CivicLens.Tests.Application.Statements;

public sealed class StatementGateCarryOverTests
{
    private const string Article = "The senator announced a rent cap for tenants.";

    [Fact]
    public void APassedGateRunCarriesItsVerifiedIssuesAndTheModelsPolicyAreas()
    {
        var run = GateRun(StatementGateReasons.Passed, Output([HousingArea, HealthArea], 0.8, (RentIssue, "a rent cap")), RentIssue);

        var carried = StatementGateCarryOver.From(run);

        Assert.Equal([RentIssue], carried.IssueIds.ToArray());
        Assert.Equal([HousingArea, HealthArea], carried.PolicyAreaIds.ToArray());
    }

    [Theory]
    [InlineData(StatementGateReasons.FailedOpen)]
    [InlineData(StatementGateReasons.OverByteCap)]
    [InlineData(StatementGateReasons.NoTrackedIssue)]
    public void AGateOutcomeThatVerifiedNothingCarriesNothing(string reason)
    {
        var run = GateRun(reason, Output([HousingArea], 0.8, (RentIssue, "a rent cap")), RentIssue);

        Assert.Equal(StatementGateCarryOver.None, StatementGateCarryOver.From(run));
    }

    [Fact]
    public void NoGateRunCarriesNothing() => Assert.Equal(StatementGateCarryOver.None, StatementGateCarryOver.From(null));

    [Fact]
    public void AStoredPassedRunWithUnreadableOutputFailsLoudInsteadOfDroppingTheIssues() =>
        Assert.Throws<InvalidDataException>(() => StatementGateCarryOver.From(GateRun(StatementGateReasons.Passed, "not json", RentIssue)));

    private static StatementGateRun GateRun(string reason, string outputJson, string issueId)
    {
        var evidence = Evidence(Article);
        var run = new CivicLens.Core.Analysis.AnalysisRun(new string('a', 32), evidence.CandidateId, StatementGateTask.Task,
            StatementGateTask.TaskVersion, StatementGateTask.Model, StatementGateTask.PromptVersion, StatementGateTask.SchemaVersion,
            evidence.Extraction.TextSha256, null, CivicLens.Core.Analysis.AnalysisRunOutcome.Classified,
            CivicLens.Core.Analysis.AnalysisTokenUsage.None, 0, "end_turn", null, outputJson, [], "{}", DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch);
        return new(run, Revision().Id, reason, [new(issueId, evidence.Extraction.ExtractionId, 0, 3)]);
    }
}
