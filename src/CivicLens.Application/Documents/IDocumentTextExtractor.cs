using CivicLens.Core.Collection;

namespace CivicLens.Application.Documents;

public interface IDocumentTextExtractor
{
    string ParserVersion { get; }
    string NormalizationVersion { get; }

    Task<string> ExtractAsync(CapturedAttemptResult attempt, string artifactRoot, CancellationToken cancellationToken);
}
