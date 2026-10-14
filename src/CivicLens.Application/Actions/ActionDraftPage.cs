using CivicLens.Application.Collection;

namespace CivicLens.Application.Actions;

public sealed record ActionDraftPage(ApiResource Resource, string JobId, string ExtractionId, string ParserVersion,
    string NormalizationVersion, string Text);
