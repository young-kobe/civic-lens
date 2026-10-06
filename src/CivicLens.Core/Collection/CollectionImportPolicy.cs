namespace CivicLens.Core.Collection;

/// <summary>Decides whether an attempt is new or duplicate and whether a 304 can link to prior captured evidence.</summary>
public sealed class CollectionImportPolicy
{
    public CollectionImportDecision Decide(CollectionAttemptResult incoming, CollectionImportDecision? existingAttempt = null,
        CapturedAttemptResult? priorCapturedAttempt = null, SentValidators? sentValidators = null)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        if (existingAttempt is not null)
            return ResolveExistingAttempt(incoming, existingAttempt, sentValidators);

        var link = incoming is NotModifiedAttemptResult notModified && priorCapturedAttempt is not null &&
            sentValidators is not null && Binds(notModified, priorCapturedAttempt, sentValidators)
                ? priorCapturedAttempt
                : null;
        return new CollectionImportDecision(incoming, link, ImportDisposition.NewAttempt, sentValidators);
    }

    private static CollectionImportDecision ResolveExistingAttempt(CollectionAttemptResult incoming, CollectionImportDecision existing,
        SentValidators? sentValidators)
    {
        if (existing.AttemptResult.AttemptId != incoming.AttemptId)
            throw new InvalidOperationException("Existing attempt result belongs to a different attempt.");
        if (!existing.AttemptResult.Equals(incoming) || existing.SentValidators != sentValidators)
            throw new InvalidOperationException("Attempt identity was reused with conflicting attempt result data or validators.");
        return new CollectionImportDecision(existing.AttemptResult, existing.PriorCapturedAttempt,
            ImportDisposition.DuplicateAttempt, existing.SentValidators);
    }

    private static bool Binds(NotModifiedAttemptResult notModified, CapturedAttemptResult prior, SentValidators validators)
    {
        if (prior.AttemptId == notModified.AttemptId || prior.ObservedAt > notModified.ObservedAt ||
            prior.SourceId != notModified.SourceId || prior.RequestedUrl != notModified.RequestedUrl ||
            prior.FinalUrl != notModified.FinalUrl || notModified.RequestedUrl != notModified.FinalUrl)
            return false;

        var priorResponse = prior.Response;
        var response = notModified.Response;
        if (response.ETag is not null && priorResponse.ETag is not null &&
            !string.Equals(response.ETag, priorResponse.ETag, StringComparison.Ordinal))
            return false;
        if (response.LastModified is not null && priorResponse.LastModified is not null &&
            response.LastModified != priorResponse.LastModified)
            return false;
        if (validators.ETag is not null)
        {
            return string.Equals(validators.ETag, priorResponse.ETag, StringComparison.Ordinal) &&
                (response.ETag is null || string.Equals(response.ETag, validators.ETag, StringComparison.Ordinal));
        }

        return validators.LastModified is not null && priorResponse.LastModified == validators.LastModified &&
            (response.LastModified is null || response.LastModified == validators.LastModified);
    }
}
