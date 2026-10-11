using CivicLens.Core.Analysis;

namespace CivicLens.Application.Analysis;

public sealed record DocumentChangeDraftingResponse(string StopReason, string? OutputJson, AnalysisTokenUsage Usage, string? RequestId);
