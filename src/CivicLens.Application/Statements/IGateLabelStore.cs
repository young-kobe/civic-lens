using CivicLens.Core.Statements;

namespace CivicLens.Application.Statements;

public interface IGateLabelStore
{
    Task AddAsync(GateLabelEntry entry, CancellationToken cancellationToken);

    Task<GateLabelQueue> ListUnlabeledAsync(int limit, CancellationToken cancellationToken);

    Task<GateLabelTask?> GetAsync(string candidateId, CancellationToken cancellationToken);

    Task<IReadOnlyList<GateLabelExportRow>> ListLabelsOfRecordAsync(CancellationToken cancellationToken);
}
