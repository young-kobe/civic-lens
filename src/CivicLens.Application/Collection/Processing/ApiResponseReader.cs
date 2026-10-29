using CivicLens.Application.Collection.Jobs;
using CivicLens.Collection.Contracts;
using CivicLens.Core.Collection;

namespace CivicLens.Application.Collection.Processing;

public sealed class ApiResponseReader(ICollectionAttemptStore attempts, ICaptureBodyReader bodies)
{
    public async Task<ApiResponse> ReadAsync(CollectionJobRecord job, string artifactRoot, CancellationToken cancellationToken)
    {
        var capture = await ReadCaptureAsync(job, cancellationToken);
        return await ReadBodyAsync(capture, artifactRoot, cancellationToken);
    }

    public async Task<ApiCapture> ReadCaptureAsync(CollectionJobRecord job, CancellationToken cancellationToken)
    {
        var jobAttempt = job.Attempts.Single(item => item.Resolution?.Outcome == CollectionJobAttemptOutcome.Succeeded);
        var stored = await attempts.GetAsync(jobAttempt.AttemptId, cancellationToken);
        return stored?.AttemptResult is CapturedAttemptResult captured
            ? new ApiCapture(jobAttempt.Request, captured)
            : throw new InvalidDataException("API attempt has no captured response.");
    }

    public async Task<ApiResponse> ReadBodyAsync(ApiCapture capture, string artifactRoot, CancellationToken cancellationToken) =>
        new(capture.Request, capture.Captured, await bodies.ReadAsync(capture.Captured, artifactRoot, cancellationToken));
}
