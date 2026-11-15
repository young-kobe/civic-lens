using CivicLens.Application.Actions;
using CivicLens.Application.Review;
using CivicLens.Core;
using CivicLens.Core.Review;
using CivicLens.Host.Analysis;

namespace CivicLens.Host.Actions;

internal sealed class ActionCommand(AnalysisQueueCommands<ActionDraftRun, ActionDraftView, ActionDraftListItem> drafts,
    RequeueBlockedActionDrafts requeueBlocked, RequeueRefusedActionDrafts requeueRefused,
    BackfillRevisions backfill, VerifyRevisions verify, Func<BackfillActionCodeText> codeText, ReviewActor owner)
{
    private const string Usage = "Use actions draft status, actions draft list [drafted|unchanged|superseded|no-official|pending|failed] [limit], " +
        "actions draft requeue <candidate-id|--all>, actions draft requeue --blocked --code <block-code>, actions draft requeue --refused, actions revisions backfill <limit>, actions revisions verify, " +
        "or actions text backfill <limit> [cursor].";
    private const int DefaultListLimit = 100;

    public static bool Matches(string[] args) => args is ["actions", ..];

    public Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken) =>
        OwnerCommand.RunAsync(async () =>
        {
            switch (args)
            {
                case ["actions", "draft", "status"]:
                    CommandOutput.Write(await drafts.Status.ExecuteAsync(owner, cancellationToken));
                    return 0;
                case ["actions", "draft", "list"]:
                    CommandOutput.Write(await drafts.List.ExecuteAsync(owner, null, DefaultListLimit, cancellationToken));
                    return 0;
                case ["actions", "draft", "list", var view]:
                    CommandOutput.Write(await drafts.List.ExecuteAsync(owner, ParseView(view), DefaultListLimit, cancellationToken));
                    return 0;
                case ["actions", "draft", "list", var view, var limit]:
                    CommandOutput.Write(await drafts.List.ExecuteAsync(owner, ParseView(view), OwnerCommand.ParseLimit(limit, Usage), cancellationToken));
                    return 0;
                case ["actions", "draft", "requeue", "--all"]:
                    CommandOutput.Write(new { requeued = await drafts.Requeue.ExecuteAsync(owner, null, cancellationToken) });
                    return 0;
                case ["actions", "draft", "requeue", "--blocked", "--code", var code]:
                    CommandOutput.Write(new { requeued = await requeueBlocked.ExecuteAsync(owner, code, cancellationToken) });
                    return 0;
                case ["actions", "draft", "requeue", "--refused"]:
                    CommandOutput.Write(new { requeued = await requeueRefused.ExecuteAsync(owner, cancellationToken) });
                    return 0;
                case ["actions", "revisions", "backfill", var limit]:
                    return await OwnerCommand.BackfillRevisionsAsync(backfill, owner, limit, Usage, cancellationToken);
                case ["actions", "revisions", "verify"]:
                    return await OwnerCommand.VerifyRevisionsAsync(verify, owner, cancellationToken);
                case ["actions", "text", "backfill", var limit, .. var cursor] when cursor.Length <= 1:
                    var (pageLimit, after) = (OwnerCommand.ParseLimit(limit, Usage), ParseCursor(cursor));
                    var moved = await codeText().ExecuteAsync(owner, pageLimit, after, cancellationToken);
                    CommandOutput.Write(moved);
                    return moved.Skipped == 0 ? 0 : 1;
                case ["actions", "draft", "requeue", "--blocked", ..]:
                    Console.Error.WriteLine(Usage);
                    return 2;
                case ["actions", "draft", "requeue", var candidateId]:
                    CommandOutput.Write(new { requeued = await drafts.Requeue.ExecuteAsync(owner, candidateId, cancellationToken) });
                    return 0;
                default:
                    Console.Error.WriteLine(Usage);
                    return 2;
            }
        });

    // The cursor is the record ID of the last examined record, as the previous page printed it.
    private static string? ParseCursor(string[] cursor) => cursor switch
    {
        [] => null,
        [var recordId] when LowercaseHex.Is(recordId, 64) => recordId,
        _ => throw new ArgumentException(Usage)
    };

    private static ActionDraftView ParseView(string text) => text switch
    {
        "drafted" => ActionDraftView.Drafted,
        "unchanged" => ActionDraftView.Unchanged,
        "superseded" => ActionDraftView.Superseded,
        "no-official" => ActionDraftView.NoTrackedOfficial,
        "pending" => ActionDraftView.Pending,
        "failed" => ActionDraftView.Failed,
        _ => throw new ArgumentException(Usage)
    };
}
