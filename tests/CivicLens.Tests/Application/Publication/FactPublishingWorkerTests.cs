using CivicLens.Application;
using CivicLens.Application.Publication;
using CivicLens.Core.Review;
using CivicLens.Tests.Fixtures;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CivicLens.Tests.Application.Publication;

public sealed class FactPublishingWorkerTests
{
    [Fact]
    public async Task AFailureThatIsNotAConflictIsReportedAndCountedSoTheOwnerSeesIt()
    {
        var scenario = ScenarioWithOneReadyAction();
        scenario.Publications.FailOnCommit = new InvalidOperationException("disk full");
        var logger = new CapturingLogger<StageWorker>();

        var result = await Worker(scenario, logger).ExecuteAsync(once: true);

        Assert.Equal(1, result.Failures);
        Assert.Equal("disk full", Assert.Single(logger.Entries).Exception!.Message);
    }

    [Fact]
    public async Task AConflictIsNotAFailureBecauseTheNextTickBuildsAgain()
    {
        var scenario = ScenarioWithOneReadyAction();
        scenario.Publications.ConflictOnCommit = true;
        var logger = new CapturingLogger<StageWorker>();

        var result = await Worker(scenario, logger).ExecuteAsync(once: true);

        Assert.Equal(0, result.Failures);
        Assert.Empty(logger.Entries);
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
        new(scenario.Publisher(), scenario.Publications, scenario.Loaders(), NullLogger<PublishCodeApprovedFacts>.Instance);

    private static StageWorker Worker(PublicationScenario scenario, ILogger<StageWorker> logger) =>
        new("factPublishing", Facts(scenario).ExecuteAsync, new CountingWakeup(() => { }), logger);
}
