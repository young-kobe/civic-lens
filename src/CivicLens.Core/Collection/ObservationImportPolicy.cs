namespace CivicLens.Core.Collection;

/// <summary>Pure policy for idempotently importing a single observation attempt.</summary>
public sealed class ObservationImportPolicy
{
    public ObservationImportPlan Plan(Observation incoming, ObservationImportPlan? existingAttempt = null,
        CapturedObservation? priorCapturedObservation = null, SentValidators? sentValidators = null)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        if (existingAttempt is not null)
            return ReplayOrReject(incoming, existingAttempt, sentValidators);

        var link = incoming is NotModifiedObservation notModified && priorCapturedObservation is not null &&
            sentValidators is not null && Binds(notModified, priorCapturedObservation, sentValidators)
                ? priorCapturedObservation
                : null;
        return new ObservationImportPlan(incoming, link, ObservationImportTransition.NewObservation, sentValidators);
    }

    private static ObservationImportPlan ReplayOrReject(Observation incoming, ObservationImportPlan existing,
        SentValidators? sentValidators)
    {
        if (existing.Observation.AttemptId != incoming.AttemptId)
            throw new InvalidOperationException("Existing observation belongs to a different attempt.");
        if (!existing.Observation.Equals(incoming) || existing.SentValidators != sentValidators)
            throw new InvalidOperationException("Attempt identity was reused with conflicting observation data or validators.");
        return new ObservationImportPlan(existing.Observation, existing.CapturedRepresentation,
            ObservationImportTransition.Replay, existing.SentValidators);
    }

    private static bool Binds(NotModifiedObservation notModified, CapturedObservation prior, SentValidators validators)
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
