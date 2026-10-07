using CivicLens.Core.Collection;
using CivicLens.Core.Documents;

namespace CivicLens.Application.Documents;

public interface IDocumentTextExtractor
{
    string ParserVersion { get; }
    string NormalizationVersion { get; }

    Task<string> ExtractAsync(CapturedAttemptResult attempt, string artifactRoot, DocumentContentProfile? profile,
        CancellationToken cancellationToken);
}
