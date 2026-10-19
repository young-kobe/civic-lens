using CivicLens.Application.Relationships;
using CivicLens.Core.Review;
using CivicLens.Host.Analysis;

namespace CivicLens.Host.Relationships;

internal sealed class RelationshipCommand(
    AnalysisQueueCommands<RelationshipDraftRun, RelationshipDraftView, RelationshipDraftListItem> drafts,
    GetRelationshipDraftStatus status, RefreshRelationshipPairs refresh, ReviewActor owner)
{
    private const string Usage = "Use relationships draft status, relationships draft list [drafted|unchanged|none|unmatched|pending|failed] " +
        "[limit], relationships draft requeue <pair-id|--all>, or relationships draft refresh.";
    private const int DefaultListLimit = 100;

    public static bool Matches(string[] args) => args is ["relationships", ..];

    public Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken) =>
        OwnerCommand.RunAsync(async () =>
        {
            switch (args)
            {
                case ["relationships", "draft", "status"]:
                    CommandOutput.Write(await status.ExecuteAsync(owner, cancellationToken));
                    return 0;
                case ["relationships", "draft", "list"]:
                    CommandOutput.Write(await drafts.List.ExecuteAsync(owner, null, DefaultListLimit, cancellationToken));
                    return 0;
                case ["relationships", "draft", "list", var view]:
                    CommandOutput.Write(await drafts.List.ExecuteAsync(owner, ParseView(view), DefaultListLimit, cancellationToken));
                    return 0;
                case ["relationships", "draft", "list", var view, var limit]:
                    CommandOutput.Write(await drafts.List.ExecuteAsync(owner, ParseView(view), OwnerCommand.ParseLimit(limit, Usage), cancellationToken));
                    return 0;
                case ["relationships", "draft", "refresh"]:
                    CommandOutput.Write(new { pairs = await refresh.ExecuteAsync(owner, cancellationToken) });
                    return 0;
                case ["relationships", "draft", "requeue", "--all"]:
                    CommandOutput.Write(new { requeued = await drafts.Requeue.ExecuteAsync(owner, null, cancellationToken) });
                    return 0;
                case ["relationships", "draft", "requeue", var pairId]:
                    CommandOutput.Write(new { requeued = await drafts.Requeue.ExecuteAsync(owner, pairId, cancellationToken) });
                    return 0;
                default:
                    Console.Error.WriteLine(Usage);
                    return 2;
            }
        });

    private static RelationshipDraftView ParseView(string text) => text switch
    {
        "drafted" => RelationshipDraftView.Drafted,
        "unchanged" => RelationshipDraftView.Unchanged,
        "none" => RelationshipDraftView.NoRelationship,
        "unmatched" => RelationshipDraftView.NoLongerMatches,
        "pending" => RelationshipDraftView.Pending,
        "failed" => RelationshipDraftView.Failed,
        _ => throw new ArgumentException(Usage)
    };
}
