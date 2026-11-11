using CivicLens.Core.ExecutiveActions;

namespace CivicLens.Infrastructure.ExecutiveActions.Persistence;

internal sealed class ExecutiveActionRevisionDispositionTargetRow
{
    public string RecordId { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public int Position { get; set; }
    public ExecutiveActionRelation Relation { get; set; }
    public ExecutiveActionKind Kind { get; set; }
    public string Number { get; set; } = string.Empty;
    public int ReferenceStart { get; set; }
    public int ReferenceLength { get; set; }
}
