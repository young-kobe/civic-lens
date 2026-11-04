using System.Globalization;
using CivicLens.Application.Briefs;
using CivicLens.Application.Review;
using CivicLens.Core.Briefs;
using CivicLens.Core.Review;

namespace CivicLens.Host.Briefs;

internal sealed class BriefCommand(RedraftBrief redraft, BackfillRevisions backfill, VerifyRevisions verify, ReviewActor owner)
{
    private const string Usage = "Use brief redraft --week <yyyy-MM-dd>, where the date is the Monday that starts the week, " +
        "brief revisions backfill <limit>, or brief revisions verify.";

    public static bool Matches(string[] args) => args is ["brief", ..];

    public Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken) =>
        OwnerCommand.RunAsync(async () =>
        {
            switch (args)
            {
                case ["brief", "redraft", "--week", var week]:
                    CommandOutput.Write(new { requeued = await redraft.ExecuteAsync(owner, ParseWeek(week), cancellationToken) });
                    return 0;
                case ["brief", "revisions", "backfill", var limit]:
                    return await OwnerCommand.BackfillRevisionsAsync(backfill, owner, limit, Usage, cancellationToken);
                case ["brief", "revisions", "verify"]:
                    return await OwnerCommand.VerifyRevisionsAsync(verify, owner, cancellationToken);
                default:
                    Console.Error.WriteLine(Usage);
                    return 2;
            }
        });

    private static DateOnly ParseWeek(string text) =>
        DateOnly.TryParseExact(text, BriefWeek.DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var week)
            ? week : throw new ArgumentException(Usage);
}
