using CivicLens.Application;
using CivicLens.Application.Publication;
using CivicLens.Core.Review;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Publication;

public sealed class FactPublishingWorkerTests
{
    [Fact]
    public async Task AFailureThatIsNotAConflictIsReportedAndCountedSoTheOwnerSeesIt()
    {
        var scenario = ScenarioWithOneReadyAction();
        scenario.Publications.FailOnCommit = new InvalidOperationException("disk full");
        var reported = new List<Exception>();

        var result = await Worker(scenario, reported.Add).ExecuteAsync(once: true);

        Assert.Equal(1, result.Failures);
        Assert.Equal("disk full", Assert.Single(reported).Message);
    }

    [Fact]
    public async Task AConflictIsNotAFailureBecauseTheNextTickBuildsAgain()
    {
        var scenario = ScenarioWithOneReadyAction();
        scenario.Publications.ConflictOnCommit = true;
        var reported = new List<Exception>();

        var result = await Worker(scenario, reported.Add).ExecuteAsync(once: true);

        Assert.Equal(0, result.Failures);
        Assert.Empty(reported);
    }

    private static PublicationScenario ScenarioWithOneReadyAction()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('a', [PublicationScenario.Decision(new string('a', 64), 1, ReviewDecisionKind.Approve)
            with { ActorSubject = ReviewAuthor.CodeSubject }]);
        scenario.Actions.Unpublished.Add(new(scenario.Actions.Publishable[action].Review, null));
        return scenario;
    }

    private static PublishCodeApprovedFacts Facts(PublicationScenario scenario) =>
        new(scenario.Publisher(), scenario.Publications, scenario.Loaders(), _ => { });

    private static StageWorker Worker(PublicationScenario scenario, Action<Exception> report) =>
        new(Facts(scenario).ExecuteAsync, new CountingWakeup(() => { }), report);
}
