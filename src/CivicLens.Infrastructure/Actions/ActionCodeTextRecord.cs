namespace CivicLens.Infrastructure.Actions;

/// <summary>A published bill record whose current revision holds retired text, with the bill keys that find its newest candidate.</summary>
internal sealed record ActionCodeTextRecord(string RecordId, string SourceId, int Congress, string BillType, int BillNumber);
