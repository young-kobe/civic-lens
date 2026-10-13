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
        var chosen = ready!.Items.Where(item => requested.Contains(item.Reference.ToString())).ToArray();
        if (chosen.Length == requested.Count) selection = chosen;
        else selectionStale = true;
    }

    private static string RecordHref(ReadyToPublishRecord item) =>
        item.Kind == PublishedRecordKind.Statement ? StatementRoutes.Editor(item.RecordId) : $"/Review/{item.RecordId}";

    private async Task PublishAsync() => Navigation.NavigateTo(await PublishTargetAsync());

    private async Task<string> PublishTargetAsync()
    {
        var cancellationToken = HttpContext.RequestAborted;
        try
        {
            var publish = Services.GetRequiredService<PublishRelease>();
            if (ConfirmIds is not { Length: > 0 } || string.IsNullOrWhiteSpace(PublishKey))
                throw new ArgumentException("Select drafts to publish.");
            var release = await publish.ExecuteAsync(actor!, new PublishReleaseRequest(
                [.. ConfirmIds.Select(PublishedRecordReference.Parse)], PublishKey),
                cancellationToken);
            return $"/Review?status=published&release={release.ReleaseNumber}#releases";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (PublicationConflictException) { return "/Review?status=publish-conflict#publish"; }
        catch (ArgumentException) { return "/Review?status=publish-invalid#publish"; }
        catch (UnauthorizedAccessException) { return "/Review?status=publish-forbidden#publish"; }
        catch (Exception) { return "/Review?status=publish-unavailable#publish"; }
    }
}
