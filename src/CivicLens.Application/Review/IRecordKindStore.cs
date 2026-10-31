using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Review;

public interface IRecordKindStore
{
    /// <summary>Gives the kind that owns the record ID, or null when no record has it. A statement candidate with no draft counts as a statement.</summary>
    Task<PublishedRecordKind?> FindKindAsync(string recordId, CancellationToken cancellationToken);
}
