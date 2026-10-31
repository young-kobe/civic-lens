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
    private const string StillOnSite = "The public site still shows this record. Reload the page and press Take off site again.";
    private const string DecisionRefused = "The decision could not be recorded, so the public site is unchanged. Reload the page, check the review state, and try again.";

    private static readonly string ReasonLimits = $"Type a reason of 1 to {PublishedCorrection.MaximumNoteLength} characters.";

    public async Task<TakeOffSiteStatus> GetStatusAsync(ReviewActor actor, PublishedRecordReference record,
        CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        var state = await publications.GetStateAsync(cancellationToken);
        if (state.Active is null) return TakeOffSiteStatus.NotPublished;
        var manifest = await PreviousRelease.LoadManifestAsync(releases, state.Active, cancellationToken);
        var withdrawal = manifest.Withdrawals.FirstOrDefault(item => item.Kind == record.Kind && item.RecordId == record.RecordId);
        return new(
            Lists(manifest, record),
            withdrawal?.WithdrawnRevisionNumber,
            withdrawal is not null && await ServedSiteListsAsync(state.Active, record, cancellationToken),
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

        var active = (await publications.GetStateAsync(cancellationToken)).Active;
        var previous = active is null ? null : await PreviousRelease.LoadAsync(releases, active, cancellationToken);
        if (previous?.FindWithdrawal(request.Record.RecordId) is not null) return await ServeAsync(request.Record, decisionSaved: false);
        if (previous?.Find(request.Record.Kind, request.Record.RecordId) is null) return NotOnSite;
        if (await CitersRefusalAsync(previous, request.Record, cancellationToken) is { } refusal) return refusal;

        var decisionSaved = false;
        if (BlockingDecision(request.Status) is { } kind)
        {
            if (!await TryRecordAsync(recordDecision, kind, decisionKey)) return DecisionRefused;
            decisionSaved = true;
        }
        var publishRequest = new PublishReleaseRequest([], publishKey) { Withdrawals = [new(request.Record.Kind, request.Record.RecordId, note)] };
        return await WithdrawAsync(actor, publishRequest, request.Record, decisionSaved);
    }

    private async Task<string?> CitersRefusalAsync(PreviousRelease previous, PublishedRecordReference record, CancellationToken cancellationToken)
    {
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

    // After the decision is saved, a closed tab must not stop the withdrawal, so these steps ignore the request token.
    private async Task<string?> WithdrawAsync(ReviewActor actor, PublishReleaseRequest publishRequest, PublishedRecordReference record,
        bool decisionSaved)
    {
        var saved = decisionSaved ? " Your decision on the record is saved." : "";
        try
        {
            _ = await publish.ExecuteAsync(actor, publishRequest, CancellationToken.None);
            return await VerifyOffSiteAsync(record);
        }
        catch (PublicationConflictException exception)
        {
            logger.LogWarning(exception, "Take off site met a publish conflict.");
            return $"Another release went out first, so the public site still shows the record.{saved} Press Take off site again.";
        }
        catch (Exception exception) when (IsServeFailure(exception))
        {
            return ServeFailure(exception, saved);
        }
        catch (ArgumentException exception)
        {
            logger.LogWarning(exception, "Take off site was refused by the publish step.");
            return $"The public site refused the withdrawal, so it still shows the record.{saved} Reload the page and check the review state and the records that cite this one. Then press Take off site again.";
        }
    }

    private async Task<string?> ServeAsync(PublishedRecordReference record, bool decisionSaved)
    {
        try
        {
            _ = await publications.ServeActiveAsync(releases.ActivateAsync, CancellationToken.None);
            return await VerifyOffSiteAsync(record);
        }
        catch (Exception exception) when (IsServeFailure(exception))
        {
            return ServeFailure(exception, decisionSaved ? " Your decision on the record is saved." : "");
        }
    }

    // A replayed key can serve an active release that holds the record again, so success needs a read of the active manifest.
    private async Task<string?> VerifyOffSiteAsync(PublishedRecordReference record)
    {
        var active = (await publications.GetStateAsync(CancellationToken.None)).Active;
        if (active is null) return null;
        var manifest = await PreviousRelease.LoadManifestAsync(releases, active, CancellationToken.None);
        return Lists(manifest, record) ? StillOnSite : null;
    }

    private async Task<bool> ServedSiteListsAsync(PublicationReleaseSummary active, PublishedRecordReference record,
        CancellationToken cancellationToken)
    {
        var served = await releases.GetActiveAsync(cancellationToken);
        if (served is null || served == active.DirectoryName) return false;
        return Lists(await PreviousRelease.LoadManifestAsync(releases, served, cancellationToken), record);
    }

    private static bool Lists(PublicationRelease manifest, PublishedRecordReference record) =>
        manifest.Records.Any(entry => entry.Kind == record.Kind && entry.RecordId == record.RecordId);

    private static bool IsServeFailure(Exception exception) => exception is IOException or UnauthorizedAccessException or Win32Exception;

    private string ServeFailure(Exception exception, string saved)
    {
        logger.LogError(exception, "Take off site could not write or serve the release.");
        return $"The public site could not be updated.{saved} Press Take off site again. If this repeats, check the release directory on the server.";
    }

    private static ReviewDecisionKind? BlockingDecision(ReviewStatus status) => status switch
    {
        ReviewStatus.Approved => ReviewDecisionKind.WithdrawApproval,
        ReviewStatus.AwaitingReview => ReviewDecisionKind.RequestChanges,
        _ => null
    };
}
