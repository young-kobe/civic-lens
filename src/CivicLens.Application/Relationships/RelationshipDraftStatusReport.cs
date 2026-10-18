using CivicLens.Application.Analysis;

namespace CivicLens.Application.Relationships;

public sealed record RelationshipDraftStatusReport(AnalysisStatusReport<RelationshipDraftView> Queue, int StatementsWithoutCongress);
