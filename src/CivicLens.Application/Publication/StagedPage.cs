namespace CivicLens.Application.Publication;

internal delegate Task<string> StagedPage(CancellationToken cancellationToken);
