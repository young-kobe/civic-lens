using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Publication;

/// <summary>Blocks a published record in review, then withdraws it from the active release.</summary>
public sealed class TakeOffSite(PublishRelease publish, IPublicationStore publications, IReleaseDirectory releases)
{
    public async Task<bool> IsOnSiteAsync(ReviewActor actor, PublishedRecordReference record, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        var active = (await publications.GetStateAsync(cancellationToken)).Active;
        if (active is null) return false;
        var manifest = await PreviousRelease.LoadManifestAsync(releases, active, cancellationToken);
        return manifest.Records.Any(entry => entry.Kind == record.Kind && entry.RecordId == record.RecordId);
    }

    /// <summary>Returns null when the record is off the site, or the sentence that tells the owner why it is not.</summary>
    public async Task<string?> ExecuteAsync(ReviewActor actor, TakeOffSiteRequest request,
        Func<ReviewDecisionKind, string, Task> recordDecision, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        ArgumentNullException.ThrowIfNull(request);
        var note = CorrectionNoteText.Validate(request.Note, nameof(request));
        var decisionKey = request.IdempotencyKey + ":decision";
        var publishKey = request.IdempotencyKey + ":publish";
        ReviewValidation.ValidateIdempotencyKey(decisionKey);
        ReviewValidation.ValidateIdempotencyKey(publishKey);
        if (!await IsOnSiteAsync(actor, request.Record, cancellationToken))
            return "This record is not on the public site.";

        var blocking = BlockingDecision(request.Status);
        if (blocking is { } kind) await recordDecision(kind, decisionKey);
        try
        {
            _ = await publish.ExecuteAsync(actor,
                new PublishReleaseRequest([], publishKey) { Withdrawals = [new(request.Record.Kind, request.Record.RecordId, note)] },
                cancellationToken);
            return null;
        }
        catch (Exception exception) when (exception is PublicationConflictException or ArgumentException)
        {
            var saved = blocking is null ? "" : " Your decision on the record is saved. Press Take off site again to finish.";
            return $"{exception.Message} The record is still on the public site.{saved}";
        }
    }

    private static ReviewDecisionKind? BlockingDecision(ReviewStatus status) => status switch
    {
        ReviewStatus.Approved => ReviewDecisionKind.WithdrawApproval,
        ReviewStatus.AwaitingReview => ReviewDecisionKind.RequestChanges,
        _ => null
    };
}
