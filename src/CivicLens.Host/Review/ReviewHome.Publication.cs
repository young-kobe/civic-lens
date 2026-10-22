using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Components;

namespace CivicLens.Host.Review;

public sealed partial class ReviewHome
{
    private const int ReleaseHistoryLimit = 5;

    private bool canPublish;
    private bool publishingOff;
    private ReadyToPublishList? ready;
    private PublicationReleaseList? releases;
    private IReadOnlyList<ReadyToPublishRecord>? selection;
    private bool selectionStale;
    private int nextReleaseNumber;

    [SupplyParameterFromQuery(Name = "publish")] private string[]? PublishSelection { get; set; }
    [SupplyParameterFromQuery(Name = "release")] private int? ReleaseNumber { get; set; }

    [SupplyParameterFromForm(FormName = "publish-confirm", Name = "records")] private string[]? ConfirmIds { get; set; }
    [SupplyParameterFromForm(FormName = "publish-confirm", Name = "notes")] private Dictionary<string, string>? ConfirmNotes { get; set; }
    [SupplyParameterFromForm(FormName = "publish-confirm", Name = "idempotencyKey")] private string? PublishKey { get; set; }

    private async Task LoadPublicationAsync(CancellationToken cancellationToken)
    {
        ready = await Services.GetRequiredService<ListPublishable>().ExecuteAsync(actor!, cancellationToken);
        releases = await Services.GetRequiredService<ListPublicationReleases>()
            .ExecuteAsync(actor!, ReleaseHistoryLimit, cancellationToken);
        nextReleaseNumber = (releases.Releases.Count == 0 ? 0 : releases.Releases.Max(release => release.ReleaseNumber)) + 1;
        ResolveSelection();
    }

    // The summary step reads the selection from the query. Only the confirm form posts and publishes.
    private void ResolveSelection()
    {
        if (PublishSelection is not { Length: > 0 }) return;
        var requested = PublishSelection.ToHashSet(StringComparer.Ordinal);
        var chosen = ready!.Items.Where(item => !item.IsWithdrawn && requested.Contains(item.Reference.ToString())).ToArray();
        if (chosen.Length == requested.Count) selection = chosen;
        else selectionStale = true;
    }

    private async Task PublishAsync() => Navigation.NavigateTo(await PublishTargetAsync());

    private async Task<string> PublishTargetAsync()
    {
        var cancellationToken = HttpContext.RequestAborted;
        try
        {
            var publish = Services.GetRequiredService<PublishRelease>();
            if (ConfirmIds is not { Length: > 0 } || string.IsNullOrWhiteSpace(PublishKey))
                throw new ArgumentException("Select records to publish.");
            var release = await publish.ExecuteAsync(actor!, new PublishReleaseRequest(
                [.. ConfirmIds.Select(PublishedRecordReference.Parse)], PublishKey)
            { Notes = ConfirmedNotes() },
                cancellationToken);
            return $"/Review?status=published&release={release.ReleaseNumber}#releases";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (PublicationConflictException) { return "/Review?status=publish-conflict#publish"; }
        catch (MissingCorrectionNoteException) { return "/Review?status=publish-note-required#publish"; }
        catch (CorrectionLimitException) { return "/Review?status=publish-correction-limit#publish"; }
        catch (ArgumentException) { return "/Review?status=publish-invalid#publish"; }
        catch (UnauthorizedAccessException) { return "/Review?status=publish-forbidden#publish"; }
        catch (Exception exception)
        {
            Logger.LogError(exception, "A release could not be published.");
            return "/Review?status=publish-unavailable#publish";
        }
    }

    private ImmutableArray<RecordNote> ConfirmedNotes() =>
        [.. (ConfirmNotes ?? []).Where(note => !string.IsNullOrWhiteSpace(note.Value)).Select(note =>
        {
            var reference = PublishedRecordReference.Parse(note.Key);
            return new RecordNote(reference.Kind, reference.RecordId, note.Value);
        })];
}
