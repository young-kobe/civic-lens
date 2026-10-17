using CivicLens.Application.Analysis;

namespace CivicLens.Application.Votes;

public sealed record VoteDraftStatusReport(AnalysisStatusReport<VoteDraftView> Queue, int WaitingForBill);
