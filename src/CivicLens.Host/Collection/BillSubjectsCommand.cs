using CivicLens.Application.Collection.Bills;
using CivicLens.Core.Review;

namespace CivicLens.Host.Collection;

internal sealed class BillSubjectsCommand(BackfillBillSubjects backfill, ReviewActor owner)
{
    private const string Usage = "Use bills subjects backfill [limit].";
    private const int DefaultLimit = 100;

    public static bool Matches(string[] args) => args is ["bills", ..];

    public Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken) =>
        OwnerCommand.RunAsync(async () =>
        {
            switch (args)
            {
                case ["bills", "subjects", "backfill"]:
                    CommandOutput.Write(await backfill.ExecuteAsync(owner, DefaultLimit, cancellationToken));
                    return 0;
                case ["bills", "subjects", "backfill", var limit]:
                    CommandOutput.Write(await backfill.ExecuteAsync(owner, OwnerCommand.ParseLimit(limit, Usage), cancellationToken));
                    return 0;
                default:
                    Console.Error.WriteLine(Usage);
                    return 2;
            }
        });
}
