using System.Collections.Immutable;
using CivicLens.Core.Briefs;

namespace CivicLens.Application.Briefs;

public sealed record BriefDraftEvidence(BriefWindow Window, ImmutableArray<BriefRecord> Records, int LeftOut, BriefRevision? Current);
