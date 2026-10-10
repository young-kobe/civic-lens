using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Collection.Contracts;
using CivicLens.Host.Components.Ui;

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
        [CollectionFailureCode.CrawlDelay] = "the site asks for a longer wait between requests"
    };

    public static IReadOnlyList<FeedItem> Build(CollectionJobRecord job,
        IReadOnlyDictionary<string, StoredCollectionAttempt> retained) =>
        job.Attempts.Reverse().Select(attempt => new FeedItem(Describe(attempt, retained.GetValueOrDefault(attempt.AttemptId)),
            attempt.CompletedAt ?? attempt.StartedAt, ToneOf(attempt, retained.ContainsKey(attempt.AttemptId)))).ToArray();

    private static string Describe(CollectionJobAttempt attempt, StoredCollectionAttempt? retained)
    {
        if (attempt.Resolution?.Receipt is { } receipt) return Describe(receipt);
        if (retained is not null)
            return $"Saved evidence is kept, but the transfer receipt is missing. Observed {Moment.Full(retained.AttemptResult.ObservedAt)}.";
        if (attempt.Resolution is null) return "No result yet. The check may still be running or waiting to recover.";
        var recorded = attempt.Resolution.ErrorCode is { } code ? $" Recorded outcome: {code}." : "";
        return $"No transfer receipt is available. Open the saved versions to find any kept evidence.{recorded}";
    }

    private static string Describe(CollectionResult receipt)
    {
        var requests = receipt.RequestCount == 1 ? "1 request" : $"{receipt.RequestCount} requests";
        var summary = receipt.Outcome switch
        {
            CollectionOutcome.Captured when receipt.Discovery is { } discovery => $"Listing saved with {discovery.Urls.Length} links found",
            CollectionOutcome.Captured => "Page saved",
            CollectionOutcome.NotModified => "The site reported no change since the last saved version",
            CollectionOutcome.Deferred => "Check put off",
            _ => "Check failed"
        };
        if (receipt.FailureCode is not { } failure) return $"{summary} after {requests}.";
        return $"{summary}: {Failures.GetValueOrDefault(failure, "the response could not be used")}. {CoverageGap}";
    }

    private static Tone ToneOf(CollectionJobAttempt attempt, bool retained) => attempt.Resolution switch
    {
        null => Tone.Live,
        { Receipt: { FailureCode: not null, Outcome: CollectionOutcome.Deferred } } => Tone.Warn,
        { Receipt.FailureCode: not null } => Tone.Bad,
        { Receipt: not null } => Tone.Ok,
        _ => retained ? Tone.Ok : Tone.Warn
    };
}
