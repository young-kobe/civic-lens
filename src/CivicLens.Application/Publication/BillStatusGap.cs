using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>The key of a bill status row with no stage, with the text and the bill type that <see cref="BillStatusMeaning.StageOf"/> needs.</summary>
public sealed record BillStatusGap(int ReleaseNumber, string RecordId, string Text, string BillType)
{
    public BillStatusStage Stage => BillStatusMeaning.StageOf(Text, BillType);
}
