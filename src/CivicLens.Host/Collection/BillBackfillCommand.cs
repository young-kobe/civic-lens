using CivicLens.Application.Collection.Bills;
using CivicLens.Core.Review;

namespace CivicLens.Host.Collection;

internal sealed class BillBackfillCommand(BackfillBillSubjects subjects, BackfillBillStatus status, ReviewActor owner)
{
    private const string Usage = "Use bills subjects backfill [limit] or bills status backfill [limit].";
    private const int DefaultLimit = 100;

    public static bool Matches(string[] args) => args is ["bills", ..];

    public Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken) =>
        OwnerCommand.RunAsync(async () =>
        {
            switch (args)
            {
                case ["bills", "subjects", "backfill", ..] when ReadLimit(args) is { } limit:
                    CommandOutput.Write(await subjects.ExecuteAsync(owner, limit, cancellationToken));
                    return 0;
                case ["bills", "status", "backfill", ..] when ReadLimit(args) is { } limit:
                    CommandOutput.Write(await status.ExecuteAsync(owner, limit, cancellationToken));
                    return 0;
                default:
                    Console.Error.WriteLine(Usage);
                    return 2;
            }
        });

    private static int? ReadLimit(string[] args) => args.Length switch
    {
        3 => DefaultLimit,
        4 => OwnerCommand.ParseLimit(args[3], Usage),
        _ => null
    };
}
