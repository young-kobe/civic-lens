using System.Collections.Immutable;
using CivicLens.Core.Briefs;

namespace CivicLens.Application.Briefs;

public sealed record PublishableBrief(BriefReview Review, ImmutableDictionary<string, BriefCitedRecord> Cited)
{
    public bool CitedRecordsChanged => Cited.Values.Any(record => record.Changed);
}
