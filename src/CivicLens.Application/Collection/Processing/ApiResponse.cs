using CivicLens.Collection.Contracts;
using CivicLens.Core.Collection;

namespace CivicLens.Application.Collection.Processing;

public sealed record ApiCapture(CollectionRequest Request, CapturedAttemptResult Captured);

public sealed record ApiResponse(CollectionRequest Request, CapturedAttemptResult Captured, byte[] Body);
