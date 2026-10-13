using CivicLens.Core.Collection;

namespace CivicLens.Application.Collection;

public interface ICaptureBodyReader
{
    Task<byte[]> ReadAsync(CapturedAttemptResult attempt, string artifactRoot, CancellationToken cancellationToken);
}
