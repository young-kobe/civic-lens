using System.Collections.Immutable;
using CivicLens.Core.Briefs;

namespace CivicLens.Application.Briefs;

public sealed record BriefReviewRead(BriefReview Review, ImmutableDictionary<string, BriefCitedRecord> Cited,
    ImmutableDictionary<string, BriefCitedRecord> SuggestionCited)
{
    public bool CitedRecordsChanged => Cited.Values.Any(record => record.Changed);
}
