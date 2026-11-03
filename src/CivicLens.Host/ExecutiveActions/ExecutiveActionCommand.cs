using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Host.ExecutiveActions;

internal sealed class ExecutiveActionCommand(BackfillRevisions backfill, VerifyRevisions verify, ReviewActor owner)
{
    private const string Usage = "Use executive-actions revisions backfill <limit>, or executive-actions revisions verify.";

    public static bool Matches(string[] args) => args is ["executive-actions", ..];

    public Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken) =>
        OwnerCommand.RunAsync(async () =>
        {
            switch (args)
            {
                case ["executive-actions", "revisions", "backfill", var limit]:
                    return await OwnerCommand.BackfillRevisionsAsync(backfill, owner, limit, Usage, cancellationToken);
                case ["executive-actions", "revisions", "verify"]:
                    return await OwnerCommand.VerifyRevisionsAsync(verify, owner, cancellationToken);
                default:
                    Console.Error.WriteLine(Usage);
                    return 2;
            }
        });
}
