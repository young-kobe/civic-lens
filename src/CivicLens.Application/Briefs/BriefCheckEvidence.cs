using System.Collections.Immutable;
using CivicLens.Core.Briefs;

namespace CivicLens.Application.Briefs;

public sealed record BriefCheckEvidence(BriefRevision Revision, ImmutableDictionary<string, BriefRecord> Cited,
    ImmutableArray<BriefRecord> NotCited, string? LastCheckedInputHash);
