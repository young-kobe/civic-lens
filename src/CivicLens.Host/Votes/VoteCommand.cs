using CivicLens.Application.Review;
using CivicLens.Application.Votes;
using CivicLens.Core;
using CivicLens.Core.Review;
using CivicLens.Host.Analysis;

namespace CivicLens.Host.Votes;

internal sealed class VoteCommand(AnalysisQueueCommands<VoteDraftRun, VoteDraftView, VoteDraftListItem> drafts, GetVoteDraftStatus status,
    RequeueBlockedVoteDrafts requeueBlocked, RequeueRefusedVoteDrafts requeueRefused, Func<BackfillVoteCodeText> codeText, ReviewActor owner)
{
    private const string Usage = "Use votes draft status, votes draft list [drafted|unchanged|superseded|no-member|pending|failed] [limit], " +
        "votes draft requeue <candidate-id|--all>, votes draft requeue --blocked --code <block-code>, votes draft requeue --refused, " +
        "or votes text backfill <limit> [cursor].";
    private const int DefaultListLimit = 100;

    public static bool Matches(string[] args) => args is ["votes", ..];

    public Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken) =>
        OwnerCommand.RunAsync(async () =>
        {
            switch (args)
            {
                case ["votes", "draft", "status"]:
                    CommandOutput.Write(await status.ExecuteAsync(owner, cancellationToken));
                    return 0;
                case ["votes", "draft", "list"]:
                    CommandOutput.Write(await drafts.List.ExecuteAsync(owner, null, DefaultListLimit, cancellationToken));
                    return 0;
                case ["votes", "draft", "list", var view]:
                    CommandOutput.Write(await drafts.List.ExecuteAsync(owner, ParseView(view), DefaultListLimit, cancellationToken));
                    return 0;
                case ["votes", "draft", "list", var view, var limit]:
                    CommandOutput.Write(await drafts.List.ExecuteAsync(owner, ParseView(view), OwnerCommand.ParseLimit(limit, Usage), cancellationToken));
                    return 0;
                case ["votes", "draft", "requeue", "--all"]:
                    CommandOutput.Write(new { requeued = await drafts.Requeue.ExecuteAsync(owner, null, cancellationToken) });
                    return 0;
                case ["votes", "draft", "requeue", "--blocked", "--code", var code]:
                    CommandOutput.Write(new { requeued = await requeueBlocked.ExecuteAsync(owner, code, cancellationToken) });
                    return 0;
                case ["votes", "draft", "requeue", "--refused"]:
                    CommandOutput.Write(new { requeued = await requeueRefused.ExecuteAsync(owner, cancellationToken) });
                    return 0;
                case ["votes", "draft", "requeue", "--blocked", ..]:
                    Console.Error.WriteLine(Usage);
                    return 2;
                case ["votes", "draft", "requeue", var candidateId]:
                    CommandOutput.Write(new { requeued = await drafts.Requeue.ExecuteAsync(owner, candidateId, cancellationToken) });
                    return 0;
                case ["votes", "text", "backfill", var limit, .. var cursor] when cursor.Length <= 1:
                    var moved = await codeText().ExecuteAsync(owner, OwnerCommand.ParseLimit(limit, Usage), ParseCursor(cursor), cancellationToken);
                    CommandOutput.Write(moved);
                    return moved.Skipped == 0 ? 0 : 1;
                default:
                    Console.Error.WriteLine(Usage);
                    return 2;
            }
        });

    // The cursor is the record ID of the last examined vote, as the previous page printed it.
    private static string? ParseCursor(string[] cursor) => cursor switch
    {
        [] => null,
        [var recordId] when LowercaseHex.Is(recordId, 64) => recordId,
        _ => throw new ArgumentException(Usage)
    };

    private static VoteDraftView ParseView(string text) => text switch
    {
        "drafted" => VoteDraftView.Drafted,
        "unchanged" => VoteDraftView.Unchanged,
        "superseded" => VoteDraftView.Superseded,
        "no-member" => VoteDraftView.NoTrackedMember,
        "pending" => VoteDraftView.Pending,
        "failed" => VoteDraftView.Failed,
        _ => throw new ArgumentException(Usage)
    };
}
