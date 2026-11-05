using CivicLens.Application.Collection.Bills;
using CivicLens.Core.Review;

namespace CivicLens.Host.Collection;

internal sealed class BillBackfillCommand(BackfillBillSubjects subjects, BackfillBillStatus status, Func<BackfillBillText> createText,
    ReviewActor owner)
{
    private const string Usage = "Use bills subjects backfill [limit], bills status backfill [limit], bills text backfill [limit], or " +
        "bills text backfill --bills <bills-file>. The bills file holds a JSON array of IDs such as 119-hr-1943.";
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
                case ["bills", "text", "backfill", "--bills", var billsFile]:
                    var bills = BillIdsFile.Read(billsFile);
                    return WriteText(await createText().ExecuteAsync(owner, bills, cancellationToken));
                case ["bills", "text", "backfill", ..] when ReadLimit(args) is { } limit:
                    return WriteText(await createText().ExecuteAsync(owner, limit, cancellationToken));
                default:
                    Console.Error.WriteLine(Usage);
                    return 2;
            }
        });

    private static int WriteText(BillTextBackfillResult result)
    {
        CommandOutput.Write(result);
        if (result.PendingActionDrafts == 0) return 0;
        Console.Error.WriteLine($"Action drafting has {result.PendingActionDrafts} pending jobs. Nothing was admitted. Run this command again after they finish.");
        return 1;
    }

    private static int? ReadLimit(string[] args) => args.Length switch
    {
        3 => DefaultLimit,
        4 => OwnerCommand.ParseLimit(args[3], Usage),
        _ => null
    };
}
