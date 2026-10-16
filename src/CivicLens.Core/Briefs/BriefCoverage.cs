namespace CivicLens.Core.Briefs;

public sealed record BriefCoverage(int Considered, int SentToModel, int Cited)
{
    public int Omitted => Considered - SentToModel;
}
