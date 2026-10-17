using CivicLens.Core.Statements;

namespace CivicLens.Application.Statements;

public sealed record GateLabelTask(string CandidateId, string ExtractionId, string Url, string Text, bool HeldOut,
    GateLabel? HumanLabel, GateLabel? ModelLabel);
