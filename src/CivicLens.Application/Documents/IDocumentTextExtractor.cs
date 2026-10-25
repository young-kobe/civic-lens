using CivicLens.Core.Collection;
using CivicLens.Core.Documents;

namespace CivicLens.Application.Documents;

public interface IDocumentTextExtractor
{
    string ExtractionVersion { get; }

    Task<string> ExtractAsync(CapturedAttemptResult attempt, string artifactRoot, DocumentContentProfile? profile,
        CancellationToken cancellationToken);

    Task<string> ExtractHtmlFragmentAsync(string markup, CancellationToken cancellationToken);
}
