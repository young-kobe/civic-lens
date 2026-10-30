namespace CivicLens.Application.Collection.Bills;

public interface IBillSubjectsBackfillStore
{
    Task<IReadOnlyList<string>> ListNewestBillsWithoutSubjectCountAsync(int limit, CancellationToken cancellationToken);

    Task<bool> AdmitSubjectsChainAsync(ConfiguredCollectionSource source, string detailJobId, int subjectCount,
        CancellationToken cancellationToken);
}
