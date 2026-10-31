using System.ComponentModel;
using CivicLens.Application.Review;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;
using Microsoft.Extensions.Logging;

namespace CivicLens.Application.Publication;

/// <summary>Blocks a published record in review, then withdraws it from the active release.</summary>
public sealed class TakeOffSite(PublishRelease publish, IPublicationStore publications, IReleaseDirectory releases,
    PublishedRecordLoaders loaders, ILogger<TakeOffSite> logger)
{
    private const string NotOnSite = "This record is not on the public site, so there is nothing to take off. Reload the page to see its current state.";
    private const string DecisionRefused = "The decision could not be recorded, so the public site is unchanged. Reload the page, check the review state, and try again.";
    private const string SameReason = "This request already went out with a different reason. Type the first reason again, or reload the page.";

    private static readonly string ReasonLimits = $"Type a reason of 1 to {PublishedCorrection.MaximumNoteLength} characters.";

    public async Task<TakeOffSiteStatus> GetStatusAsync(ReviewActor actor, PublishedRecordReference record,
        CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        var state = await publications.GetStateAsync(cancellationToken);
        if (state.Active is null) return TakeOffSiteStatus.NotPublished;
        var manifest = await PreviousRelease.LoadManifestAsync(releases, state.Active, cancellationToken);
        var takenOff = manifest.Withdrawals.Any(withdrawal => withdrawal.Kind == record.Kind && withdrawal.RecordId == record.RecordId);
        return new(
            manifest.Records.Any(entry => entry.Kind == record.Kind && entry.RecordId == record.RecordId),
            takenOff,
            takenOff && await releases.GetActiveAsync(cancellationToken) != state.Active.DirectoryName,
            state.Active.ReleaseNumber != state.LatestReleaseNumber);
    }

    /// <summary>Returns null when the record is off the site, or a plain sentence that tells the owner what happened and what to do next.</summary>
    public async Task<string?> ExecuteAsync(ReviewActor actor, TakeOffSiteRequest request,
        Func<ReviewDecisionKind, string, Task> recordDecision, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        ArgumentNullException.ThrowIfNull(request);
        ReviewValidation.ValidateIdempotencyKey(request.IdempotencyKey);
        var decisionKey = request.IdempotencyKey + ":decision";
        var publishKey = request.IdempotencyKey + ":publish";
        ReviewValidation.ValidateIdempotencyKey(decisionKey);
        ReviewValidation.ValidateIdempotencyKey(publishKey);
        string note;
        try
        {
            note = CorrectionNoteText.Validate(request.Note, nameof(request));
        }
        catch (ArgumentException)
        {
            return ReasonLimits;
        }
        var publishRequest = new PublishReleaseRequest([], publishKey) { Withdrawals = [new(request.Record.Kind, request.Record.RecordId, note)] };

        bool committed;
        try
        {
            committed = await publications.FindReplayAsync(actor.Subject, publishKey, ReviewValidation.HashPayload(publishRequest), cancellationToken) is not null;
        }
        catch (ArgumentException)
        {
            return SameReason;
        }
        var decisionSaved = false;
        if (!committed)
        {
            if (await RefuseBeforeDecisionAsync(request.Record, cancellationToken) is { } refusal) return refusal;
            if (BlockingDecision(request.Status) is { } kind)
            {
                if (!await TryRecordAsync(recordDecision, kind, decisionKey)) return DecisionRefused;
                decisionSaved = true;
            }
        }
        return await WithdrawAsync(actor, publishRequest, decisionSaved);
    }

    private async Task<string?> RefuseBeforeDecisionAsync(PublishedRecordReference record, CancellationToken cancellationToken)
    {
        var active = (await publications.GetStateAsync(cancellationToken)).Active;
        if (active is null) return NotOnSite;
        var previous = await PreviousRelease.LoadAsync(releases, active, cancellationToken);
        if (previous.Find(record.Kind, record.RecordId) is null) return NotOnSite;
        var citers = await WithdrawalCascade.FindCitersAsync(releases, loaders, previous, [record], new HashSet<string>(), cancellationToken);
        if (citers.Count == 0) return null;
        var names = citers.Select(citer => $"\"{previous.Find(citer.Kind, citer.RecordId)!.Headline}\" ({citer})");
        return $"The public site still shows records that cite this one: {string.Join(", ", names)}. No decision was written. Take those records off first. Then take this record off.";
    }

    private async Task<bool> TryRecordAsync(Func<ReviewDecisionKind, string, Task> recordDecision, ReviewDecisionKind kind, string key)
    {
        try
        {
            await recordDecision(kind, key);
            return true;
        }
        catch (ArgumentException exception)
        {
            logger.LogWarning(exception, "Take off site could not record the {Decision} decision.", kind);
            return false;
        }
    }

    private async Task<string?> WithdrawAsync(ReviewActor actor, PublishReleaseRequest publishRequest, bool decisionSaved)
    {
        var saved = decisionSaved ? " Your decision on the record is saved." : "";
        try
        {
            _ = await publish.ExecuteAsync(actor, publishRequest, CancellationToken.None);
            return null;
        }
        catch (PublicationConflictException exception)
        {
            logger.LogWarning(exception, "Take off site met a publish conflict.");
            return $"Another release went out first, so the public site still shows the record.{saved} Press Take off site again.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception)
        {
            logger.LogError(exception, "Take off site could not write or serve the release.");
            return $"The public site could not be updated.{saved} Press Take off site again. If this repeats, ask the operator to check the release directory.";
        }
        catch (ArgumentException exception)
        {
            logger.LogWarning(exception, "Take off site was refused by the publish step.");
            return $"The public site refused the withdrawal, so it still shows the record.{saved} Reload the page and check the review state and the records that cite this one. Then press Take off site again.";
        }
    }

    private static ReviewDecisionKind? BlockingDecision(ReviewStatus status) => status switch
    {
        ReviewStatus.Approved => ReviewDecisionKind.WithdrawApproval,
        ReviewStatus.AwaitingReview => ReviewDecisionKind.RequestChanges,
        _ => null
    };
}
