using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Discovery;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Collection.Contracts;
using CivicLens.Core.Collection;
using CivicLens.PublicWeb.Components.Ui;

namespace CivicLens.Host.Collection;

public static class CheckTimeline
{
    private const string CoverageGap = "This is a coverage gap, not evidence of no activity.";

    private static readonly Dictionary<CollectionFailureCode, string> Failures = new()
    {
        [CollectionFailureCode.RobotsUnavailable] = "the site rules file could not be read",
        [CollectionFailureCode.RobotsDenied] = "the site rules do not allow this page",
        [CollectionFailureCode.OutOfScope] = "the page led outside the allowed address",
        [CollectionFailureCode.RateLimited] = "the site asked us to slow down",
        [CollectionFailureCode.HttpError] = "the site returned an error",
        [CollectionFailureCode.Cancelled] = "the check was stopped",
        [CollectionFailureCode.Timeout] = "the site took too long to answer",
        [CollectionFailureCode.RequestBudget] = "the request limit was reached",
        [CollectionFailureCode.Oversized] = "the page was larger than the size limit",
        [CollectionFailureCode.TransportError] = "the connection failed",
        [CollectionFailureCode.CrawlDelay] = "the site asks for a longer wait between requests",
        [CollectionFailureCode.CredentialUnavailable] = "the access key for this source is not set"
    };

    public static IReadOnlyList<FeedItem> Build(CollectionJobRecord job,
        IReadOnlyDictionary<string, StoredCollectionAttempt> retained) =>
        job.Attempts.Reverse().Select(attempt => ToItem(attempt, retained.GetValueOrDefault(attempt.AttemptId))).ToArray();

    private static FeedItem ToItem(CollectionJobAttempt attempt, StoredCollectionAttempt? retained) =>
        new(Describe(attempt, retained), attempt.CompletedAt ?? attempt.StartedAt, ToneOf(attempt, retained));

    private static string Describe(CollectionJobAttempt attempt, StoredCollectionAttempt? retained)
    {
        if (retained is not null && attempt.Transfer is { } transfer) return Describe(retained, transfer);
        if (retained is not null)
            return $"Saved evidence is kept. Its transfer was not counted, so the full reservation is charged. Observed {Moment.Full(retained.AttemptResult.ObservedAt)}.";
        if (attempt.Resolution is null) return "No result yet. The check may still be running or waiting to recover.";
        var recorded = attempt.Resolution.ErrorCode is { } code ? $" Recorded outcome: {code}." : "";
        return $"No transfer receipt is available. Open the saved versions to find any kept evidence.{recorded}";
    }

    private static string Describe(StoredCollectionAttempt retained, AttemptTransfer transfer)
    {
        var requests = transfer.RequestCount == 1 ? "1 request" : $"{transfer.RequestCount} requests";
        var summary = retained.AttemptResult switch
        {
            CapturedAttemptResult when retained.Discovery is { } discovery => $"Listing saved with {discovery.Urls.Length} links found{Skipped(discovery)}",
            CapturedAttemptResult => "Page saved",
            NotModifiedAttemptResult => "The site reported no change since the last saved version",
            DeferredAttemptResult => "Check put off",
            _ => "Check failed"
        };
        if (FailureCodeOf(retained.AttemptResult) is not { } failure) return $"{summary} after {requests}.";
        return $"{summary}: {FailureText(failure)}. {CoverageGap}";
    }

    private static string? FailureCodeOf(CollectionAttemptResult result) => result switch
    {
        FailedAttemptResult failed => failed.FailureCode,
        DeferredAttemptResult deferred => deferred.FailureCode,
        _ => null
    };

    private static string FailureText(string failureCode) =>
        Enum.TryParse<CollectionFailureCode>(failureCode, out var failure) && Failures.TryGetValue(failure, out var text)
            ? text : "the response could not be used";

    private static string Skipped(DiscoveryEvidence discovery) =>
        discovery.OutOfScopeCount > 0 ? $" and {discovery.OutOfScopeCount} outside the source scope skipped" : "";

    private static Tone ToneOf(CollectionJobAttempt attempt, StoredCollectionAttempt? retained) => (attempt.Resolution, retained?.AttemptResult) switch
    {
        (null, _) => Tone.Live,
        (_, DeferredAttemptResult) => Tone.Warn,
        (_, FailedAttemptResult) => Tone.Bad,
        (_, not null) => Tone.Ok,
        _ => Tone.Warn
    };
}
