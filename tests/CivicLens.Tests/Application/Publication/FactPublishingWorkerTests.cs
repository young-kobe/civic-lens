using CivicLens.Application;
using CivicLens.Application.Publication;
using CivicLens.Core.Review;

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

    [Fact]
    public async Task ARunThatIsNotOnceSleepsUntilTheWakeupAndThenTicksAgain()
    {
        var scenario = ScenarioWithOneReadyAction();
        using var stop = new CancellationTokenSource();
        var wakeup = new CountingWakeup(() => { if (scenario.Publications.Committed.Count > 0) stop.Cancel(); });

        var result = await new FactPublishingWorker(Facts(scenario), wakeup, _ => { }).ExecuteAsync(once: false, stop.Token);

        Assert.Equal((1, 1, 1), (result.Passes, result.ProgressCount, wakeup.WaitCount));
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

    private static FactPublishingWorker Worker(PublicationScenario scenario, Action<Exception> report) =>
        new(Facts(scenario), new CountingWakeup(() => { }), report);

    private sealed class CountingWakeup(Action onWait) : IWorkerWakeup
    {
        public int WaitCount { get; private set; }
        public Task ConnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task WaitAsync(CancellationToken cancellationToken)
        {
            WaitCount++;
            onWait();
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
