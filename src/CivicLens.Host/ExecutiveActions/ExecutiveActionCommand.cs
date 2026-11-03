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
                    var backfilled = await backfill.ExecuteAsync(owner, OwnerCommand.ParseLimit(limit, Usage), cancellationToken);
                    CommandOutput.Write(backfilled);
                    return backfilled.Skipped == 0 ? 0 : 1;
                case ["executive-actions", "revisions", "verify"]:
                    var verification = await verify.ExecuteAsync(owner, cancellationToken);
                    CommandOutput.Write(verification);
                    return verification.Mismatched == 0 && verification.Legacy == 0 ? 0 : 1;
                default:
                    Console.Error.WriteLine(Usage);
                    return 2;
            }
        });
}
