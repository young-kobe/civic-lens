using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Host.Review;

internal sealed class DocumentChangeCommand(BackfillRevisions backfill, VerifyRevisions verify, ReviewActor owner)
{
    private const string Usage = "Use document-changes revisions backfill <limit>, or document-changes revisions verify.";

    public static bool Matches(string[] args) => args is ["document-changes", ..];

    public Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken) =>
        OwnerCommand.RunAsync(async () =>
        {
            switch (args)
            {
                case ["document-changes", "revisions", "backfill", var limit]:
                    return await OwnerCommand.BackfillRevisionsAsync(backfill, owner, limit, Usage, cancellationToken);
                case ["document-changes", "revisions", "verify"]:
                    return await OwnerCommand.VerifyRevisionsAsync(verify, owner, cancellationToken);
                default:
                    Console.Error.WriteLine(Usage);
                    return 2;
            }
        });
}
