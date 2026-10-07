using CivicLens.Core.Collection;
using CivicLens.Core.Documents;
using CivicLens.Application.Collection;

namespace CivicLens.Application.Documents;

public sealed class ExtractDocument(ICollectionAttemptStore attempts, IDocumentTextExtractor extractor,
    IDocumentExtractionStore extractions)
{
    public async Task<DocumentExtraction> ExecuteAsync(string attemptId, string artifactRoot,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(attemptId, artifactRoot, cancellationToken);
        var captured = await GetCapturedAttemptAsync(attemptId, cancellationToken);
        return await ExtractAsync(captured, artifactRoot, profile: null, cancellationToken);
    }

    public async Task<DocumentExtraction> ExecuteAsync(string attemptId, string artifactRoot,
        DocumentContentProfile profile, CancellationToken cancellationToken = default)
    {
        ValidateRequest(attemptId, artifactRoot, cancellationToken);
        ArgumentNullException.ThrowIfNull(profile);
        var captured = await GetCapturedAttemptAsync(attemptId, cancellationToken);
        return await ExtractAsync(captured, artifactRoot, profile, cancellationToken);
    }

    public async Task<DocumentExtraction> ExecuteConfiguredAsync(CollectionConfiguration configuration, string attemptId,
        string artifactRoot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ValidateRequest(attemptId, artifactRoot, cancellationToken);
        configuration = CollectionConfigurationRevision.Create(configuration).ReadConfiguration();
        var captured = await GetCapturedAttemptAsync(attemptId, cancellationToken);
        var source = configuration.Sources.SingleOrDefault(candidate => candidate.Id == captured.SourceId);
        if (source is null)
            throw new ArgumentException("No configured source matches the captured attempt.", nameof(attemptId));
        if (string.IsNullOrWhiteSpace(source.DocumentProfileId))
            throw new ArgumentException($"Source '{source.Id}' has no document profile assignment.", nameof(configuration));
        var profileConfiguration = configuration.DocumentProfiles!.Single(profile => profile.Id == source.DocumentProfileId);
        return await ExtractAsync(captured, artifactRoot, profileConfiguration.ToProfile(), cancellationToken);
    }

    private async Task<CapturedAttemptResult> GetCapturedAttemptAsync(string attemptId, CancellationToken cancellationToken)
    {
        var stored = await attempts.GetAsync(attemptId, cancellationToken);
        if (stored?.AttemptResult is not CapturedAttemptResult captured)
            throw new ArgumentException("Attempt must be an imported captured result.", nameof(attemptId));
        return captured;
    }

    private async Task<DocumentExtraction> ExtractAsync(CapturedAttemptResult captured, string artifactRoot,
        DocumentContentProfile? profile, CancellationToken cancellationToken)
    {
        var text = await extractor.ExtractAsync(captured, artifactRoot, profile, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var extraction = new DocumentExtraction(captured, extractor.ParserVersion, extractor.NormalizationVersion, text, profile);
        return await extractions.SaveAsync(extraction, cancellationToken);
    }

    private static void ValidateRequest(string attemptId, string artifactRoot, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attemptId);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactRoot);
        cancellationToken.ThrowIfCancellationRequested();
    }
}
