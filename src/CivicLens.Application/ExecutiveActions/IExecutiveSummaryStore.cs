using CivicLens.Application.Analysis;

namespace CivicLens.Application.ExecutiveActions;

public interface IExecutiveSummaryStore : IAnalysisQueueStore<ExecutiveSummaryRun, ExecutiveSummaryView, ExecutiveSummaryListItem>
{
    /// <summary>Reads the current revision and the body text of a record. It is null when the record or its text is missing.</summary>
    Task<ExecutiveSummaryEvidence?> ReadEvidenceAsync(string recordId, CancellationToken cancellationToken);

    /// <summary>Finds the record of an original document by its number, for the measure command. It is null when the document is not recorded.</summary>
    Task<string?> FindRecordIdAsync(string documentNumber, CancellationToken cancellationToken);
}
