using CivicLens.Collection.Contracts;
using CivicLens.Core.Collection;

namespace CivicLens.Application.Collection.Processing;

public sealed record ApiResponse(CollectionRequest Request, CapturedAttemptResult Captured, byte[] Body);
