using CivicLens.Application.Collection.FederalRegister;
using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Host.ExecutiveActions;

internal sealed class ExecutiveActionCommand(BackfillRevisions backfill, VerifyRevisions verify, BackfillRevisions backfillTargets, VerifyRevisions verifyTargets,
    Func<BackfillFederalRegisterText> createText, ReviewActor owner)
{
    private const string Usage = "Use executive-actions revisions backfill <limit>, executive-actions revisions verify, executive-actions targets backfill <limit>, executive-actions targets verify, or executive-actions text backfill [limit].";
    private const int DefaultTextLimit = 100;

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
                case ["executive-actions", "targets", "backfill", var limit]:
                    return await OwnerCommand.BackfillRevisionsAsync(backfillTargets, owner, limit, Usage, cancellationToken);
                case ["executive-actions", "targets", "verify"]:
                    return await OwnerCommand.VerifyRevisionsAsync(verifyTargets, owner, cancellationToken);
                case ["executive-actions", "text", "backfill", ..] when args.Length <= 4:
                    var textLimit = args.Length == 4 ? OwnerCommand.ParseLimit(args[3], Usage) : DefaultTextLimit;
                    CommandOutput.Write(await createText().ExecuteAsync(owner, textLimit, cancellationToken));
                    return 0;
                default:
                    Console.Error.WriteLine(Usage);
                    return 2;
            }
        });
}
