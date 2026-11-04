using CivicLens.Application.Analysis;
using CivicLens.Core.Analysis;

namespace CivicLens.Tests.Fixtures;

internal static class AnalysisRunInputs
{
    public static readonly AnalysisRunInput Sample = new("System prompt.", "User message.", """{"type":"object"}""", AnalysisEffort.Medium, 4_000);
}
