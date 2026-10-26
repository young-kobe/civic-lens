using CivicLens.Application.Collection.Votes;

namespace CivicLens.Application.Collection.Jobs;

public static class CollectionJobPriority
{
    public const int Check = 1;
    public const int Backfill = 3;

    private const int LiveBillDays = 2;

    public static bool IsValid(int priority) => priority is Check or Backfill;

    public static int OfBillDay(DateOnly day, DateOnly today) =>
        day >= today.AddDays(-LiveBillDays) ? Check : Backfill;

    public static int OfSession(VoteSession session, DateOnly today) =>
        session.Year < today.Year ? Backfill : Check;
}
