using CivicLens.Application.Collection.Jobs;
using CivicLens.Collection.Contracts;
using CivicLens.Core.Collection;

namespace CivicLens.Application.Collection.Processing;

public sealed class ApiResponseReader(ICollectionAttemptStore attempts, ICaptureBodyReader bodies)
{
    public async Task<ApiResponse> ReadAsync(CollectionJobRecord job, string artifactRoot, CancellationToken cancellationToken)
    {
        var jobAttempt = job.Attempts.Single(item => item.Resolution?.Outcome == CollectionJobAttemptOutcome.Succeeded);
        var stored = await attempts.GetAsync(jobAttempt.AttemptId, cancellationToken);
        if (stored?.AttemptResult is not CapturedAttemptResult captured)
            throw new InvalidDataException("API attempt has no captured response.");
        return new ApiResponse(jobAttempt.Request, captured, await bodies.ReadAsync(captured, artifactRoot, cancellationToken));
    }
}
