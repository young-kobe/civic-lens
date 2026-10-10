using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Health;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Collection.Processing;
using CivicLens.Core.Review;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Collection.Health;

public sealed class SourceHealthTests
{
    private static readonly ReviewActor Owner = new("auth0|owner", ReviewRole.Owner);
    private static readonly DateTimeOffset Created = new(2026, 10, 9, 8, 0, 0, TimeSpan.Zero);
    private static readonly string[] SourceIds = ["never", "checking", "retrying", "failed", "cancelled", "changed", "baseline", "current", "blocked"];

    [Fact]
    public async Task EachSourceReportsItsLatestCheckAndOnlyTerminalFailuresNeedAttention()
    {
        var jobs = Jobs();
        var store = new FixedJobStore { Latest = jobs.Values.ToArray() };
        var processing = new FixedProcessingStore { Records = Records(jobs) };
        var workspace = Workspace(store, processing);

        var report = await workspace.GetSourceHealthAsync(Owner, default);

        var bySource = report.Sources.ToDictionary(item => item.SourceId);
        Assert.Equal(SourceIds, report.Sources.Select(item => item.SourceId));
        Assert.Equal(SourceCheckState.NeverChecked, bySource["never"].State);
        Assert.Null(bySource["never"].LastCheckedAt);
        Assert.Equal(SourceCheckState.Checking, bySource["checking"].State);
        Assert.Equal(SourceCheckState.Failed, bySource["retrying"].State);
        Assert.Equal(Created.AddMinutes(5), bySource["retrying"].RetryAt);
        Assert.False(bySource["retrying"].NeedsAttention);
        Assert.Equal(SourceCheckState.Failed, bySource["failed"].State);
        Assert.Null(bySource["failed"].RetryAt);
        Assert.True(bySource["failed"].NeedsAttention);
        Assert.Equal(SourceCheckState.Cancelled, bySource["cancelled"].State);
        Assert.Equal(SourceCheckState.ChangeFound, bySource["changed"].State);
        Assert.Equal(new string('c', 64), bySource["changed"].ComparisonId);
        Assert.Equal(SourceCheckState.BaselineSaved, bySource["baseline"].State);
        Assert.Equal(SourceCheckState.UpToDate, bySource["current"].State);
        Assert.Equal(SourceCheckState.Failed, bySource["blocked"].State);
        Assert.True(bySource["blocked"].NeedsAttention);
        Assert.Equal(2, report.AttentionCount);
    }

    [Fact]
    public async Task LastCheckTimeIsTheLatestAttemptCompletionElseCreation()
    {
        var completed = Created.AddMinutes(3);
        var job = Job("current", CollectionJobState.Succeeded) with
        {
            Attempts = [Attempt(Created.AddMinutes(1)), Attempt(completed)]
        };
        var workspace = Workspace(new FixedJobStore { Latest = [job] }, new FixedProcessingStore());

        var report = await workspace.GetSourceHealthAsync(Owner, default);

        Assert.Equal(completed, report.Sources.Single(item => item.SourceId == "current").LastCheckedAt);
        Assert.Equal(Created, (await Workspace(new FixedJobStore { Latest = [Job("checking", CollectionJobState.Running)] },
            new FixedProcessingStore()).GetSourceHealthAsync(Owner, default)).Sources.Single(item => item.SourceId == "checking").LastCheckedAt);
    }

    [Fact]
    public async Task ChangedPageWinsOverEarlierPagesOfTheSameCheck()
    {
        var job = Job("changed", CollectionJobState.Succeeded);
        var records = new[]
        {
            Record(job, "a-first", EvidenceProcessingStatus.Succeeded, EvidenceProcessingOutcome.Unchanged),
            Record(job, "b-second", EvidenceProcessingStatus.Succeeded, EvidenceProcessingOutcome.Changed, new string('d', 64))
        };
        var workspace = Workspace(new FixedJobStore { Latest = [job] }, new FixedProcessingStore { Records = records });

        var report = await workspace.GetSourceHealthAsync(Owner, default);

        Assert.Equal(new string('d', 64), report.Sources.Single(item => item.SourceId == "changed").ComparisonId);
    }

    [Fact]
    public async Task OnlyTheOwnerMayReadSourceHealth()
    {
        var workspace = Workspace(new FixedJobStore(), new FixedProcessingStore());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            workspace.GetSourceHealthAsync(new ReviewActor("auth0|reviewer", ReviewRole.Reviewer), default));
    }

    // The newest page already holds the latest check of the sources it covers, so only the rest are read again.
    [Fact]
    public async Task FirstJobPageIsReusedForHealthAndOnlyMissingSourcesAreRead()
    {
        var onPage = Job("checking", CollectionJobState.Running);
        var store = new FixedJobStore { Page = [onPage], Latest = [Job("failed", CollectionJobState.Failed)] };
        var workspace = Workspace(store, new FixedProcessingStore());

        var result = await workspace.GetSourcesAsync(Owner, null, null, 10, default);

        var read = Assert.Single(store.LatestReads);
        Assert.DoesNotContain(read, target => target.SourceId == "checking");
        Assert.Equal(SourceIds.Length - 1, read.Count);
        Assert.Equal(SourceCheckState.Checking, result.Health!.Sources.Single(item => item.SourceId == "checking").State);
        Assert.Equal(SourceCheckState.Failed, result.Health.Sources.Single(item => item.SourceId == "failed").State);
    }

    // An older page says nothing about which check is the latest, so it must never feed the health report.
    [Fact]
    public async Task OlderJobPageNeverFeedsHealth()
    {
        var olderCheck = Job("checking", CollectionJobState.Failed);
        var store = new FixedJobStore { Page = [olderCheck], Latest = [Job("checking", CollectionJobState.Running)] };
        var processing = new FixedProcessingStore();
        var workspace = Workspace(store, processing);
        var cursor = "o." + Created.UtcTicks + ".00000000000000000000000000000001";

        var result = await workspace.GetSourcesAsync(Owner, null, cursor, 10, default);

        Assert.Equal(SourceIds.Length, Assert.Single(store.LatestReads).Count);
        Assert.Equal(SourceCheckState.Checking, result.Health!.Sources.Single(item => item.SourceId == "checking").State);
    }

    // Recent checks show each check's own result, while the source row shows only its latest check.
    [Fact]
    public async Task EachListedCheckKeepsItsOwnResultAndSourceHealthNamesTheLatestCheck()
    {
        var newer = Job("changed", CollectionJobState.Succeeded);
        var older = Job("changed", CollectionJobState.Failed);
        var store = new FixedJobStore { Page = [newer, older] };
        var processing = new FixedProcessingStore
        {
            Records = [Record(newer, "page", EvidenceProcessingStatus.Succeeded, EvidenceProcessingOutcome.Changed, new string('e', 64))]
        };

        var result = await Workspace(store, processing).GetSourcesAsync(Owner, null, null, 10, default);

        Assert.Equal(SourceCheckState.ChangeFound, result.ChecksByJobId[newer.JobId].State);
        Assert.Equal(new string('e', 64), result.ChecksByJobId[newer.JobId].ComparisonId);
        Assert.Equal(SourceCheckState.Failed, result.ChecksByJobId[older.JobId].State);
        Assert.True(result.ChecksByJobId[older.JobId].NeedsAttention);
        var source = result.Health.Sources.Single(item => item.SourceId == "changed");
        Assert.Equal(newer.JobId, source.JobId);
        Assert.False(source.NeedsAttention);
    }

    [Fact]
    public async Task SourcesRejectsTamperedCursorAsInvalidInputBeforeReading()
    {
        var store = new FixedJobStore();
        var workspace = Workspace(store, new FixedProcessingStore());

        await Assert.ThrowsAsync<ArgumentException>(() => workspace.GetSourcesAsync(Owner, null, "tampered", 10, default));
        Assert.Equal(0, store.PageReads);
    }

    private static Dictionary<string, CollectionJobRecord> Jobs() => new()
    {
        ["checking"] = Job("checking", CollectionJobState.Running),
        ["retrying"] = Job("retrying", CollectionJobState.WaitingToRetry) with { RetryAt = Created.AddMinutes(5) },
        ["failed"] = Job("failed", CollectionJobState.Failed),
        ["cancelled"] = Job("cancelled", CollectionJobState.Cancelled),
        ["changed"] = Job("changed", CollectionJobState.Succeeded),
        ["baseline"] = Job("baseline", CollectionJobState.Succeeded),
        ["current"] = Job("current", CollectionJobState.Succeeded),
        ["blocked"] = Job("blocked", CollectionJobState.Succeeded)
    };

    private static EvidenceProcessingRecord[] Records(Dictionary<string, CollectionJobRecord> jobs) =>
    [
        Record(jobs["changed"], "page", EvidenceProcessingStatus.Succeeded, EvidenceProcessingOutcome.Changed, new string('c', 64)),
        Record(jobs["baseline"], "page", EvidenceProcessingStatus.Succeeded, EvidenceProcessingOutcome.Baseline),
        Record(jobs["current"], "page", EvidenceProcessingStatus.Succeeded, EvidenceProcessingOutcome.Unchanged),
        Record(jobs["blocked"], "page", EvidenceProcessingStatus.Blocked, EvidenceProcessingOutcome.Blocked)
    ];

    private static EvidenceProcessingRecord Record(CollectionJobRecord job, string attemptId, EvidenceProcessingStatus status,
        EvidenceProcessingOutcome outcome, string? comparisonId = null) => new(job.JobId, attemptId,
            job.Definition.SourceId, job.Definition.Url, EvidenceProcessingStage.Complete, status, null, 0, 0, null,
            null, comparisonId, outcome, null);

    private static CollectionJobRecord Job(string sourceId, CollectionJobState state) => new(
        Guid.NewGuid().ToString("N"), CollectionJobDefinition.FromConfiguration(Configuration, sourceId), "key-" + sourceId,
        state, Created, null, false, 0, 0, 0, []);

    private static CollectionJobAttempt Attempt(DateTimeOffset completedAt) => new(Guid.NewGuid().ToString("N"),
        new() { JobId = "job", SourceId = "current", Url = Url("current"), AllowedOrigin = "https://example.test", AllowedPathPrefix = "/news", ArtifactDirectory = Path.GetTempPath() },
        null, Created, completedAt);

    private static CollectionWorkspace Workspace(FixedJobStore jobs, FixedProcessingStore processing) =>
        new(Configuration, jobs, null!, processing);

    private static string Url(string sourceId) => "https://example.test/news/" + sourceId;

    private static CollectionConfiguration Configuration => new()
    {
        Version = 1,
        People = [new PersonConfiguration { Id = "person", Name = "Official" }],
        Sources = [.. SourceIds.Select(id => new WatchedSourceConfiguration
        {
            Id = id,
            Url = Url(id),
            AllowedOrigin = "https://example.test",
            AllowedPathPrefix = "/news",
            PersonIds = ["person"]
        })]
    };
}
