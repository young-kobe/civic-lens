using System.Linq.Expressions;

namespace CivicLens.Infrastructure.Collection.BillStream;

internal static class ActionCandidateInputs
{
    public static Expression<Func<ActionCandidateRow, bool>> AtLeast(bool includesSummaries, bool includesSubjects) =>
        row => (row.IncludesSummaries || !includesSummaries) && (row.IncludesSubjects || !includesSubjects);

    public static Expression<Func<ActionCandidateRow, bool>> Supersedes(long updateDateUtcTicks, bool includesSummaries, bool includesSubjects) =>
        row => row.UpdateDateUtcTicks > updateDateUtcTicks ||
            row.UpdateDateUtcTicks == updateDateUtcTicks && (row.IncludesSummaries || !includesSummaries) && (row.IncludesSubjects || !includesSubjects) &&
            (row.IncludesSummaries != includesSummaries || row.IncludesSubjects != includesSubjects);
}
