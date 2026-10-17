using System.Globalization;
using CivicLens.Application.Votes;
using CivicLens.Core.Review;
using CivicLens.Host.Analysis;

namespace CivicLens.Host.Votes;

internal sealed class VoteCommand(AnalysisQueueCommands<VoteDraftRun, VoteDraftView, VoteDraftListItem> drafts, GetVoteDraftStatus status,
    RequeueBlockedVoteDrafts requeueBlocked, ReviewActor owner)
{
    private const string Usage = "Use votes draft status, votes draft list [drafted|unchanged|superseded|no-member|pending|failed] [limit], " +
        "votes draft requeue <candidate-id|--all>, or votes draft requeue --blocked --code <block-code>.";
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
                    CommandOutput.Write(await drafts.List.ExecuteAsync(owner, ParseView(view), ParseLimit(limit), cancellationToken));
                    return 0;
                case ["votes", "draft", "requeue", "--all"]:
                    CommandOutput.Write(new { requeued = await drafts.Requeue.ExecuteAsync(owner, null, cancellationToken) });
                    return 0;
                case ["votes", "draft", "requeue", "--blocked", "--code", var code]:
                    CommandOutput.Write(new { requeued = await requeueBlocked.ExecuteAsync(owner, code, cancellationToken) });
                    return 0;
                case ["votes", "draft", "requeue", "--blocked", ..]:
                    Console.Error.WriteLine(Usage);
                    return 2;
                case ["votes", "draft", "requeue", var candidateId]:
                    CommandOutput.Write(new { requeued = await drafts.Requeue.ExecuteAsync(owner, candidateId, cancellationToken) });
                    return 0;
                default:
                    Console.Error.WriteLine(Usage);
                    return 2;
            }
        });

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

    private static int ParseLimit(string text) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var limit) ? limit : throw new ArgumentException(Usage);
}
