using CivicLens.Application.Collection;
using CivicLens.Core.Votes;

namespace CivicLens.Application.Votes;

public static class VoteSources
{
    public const string SenateRecordKey = "senate-votes";

    public static VoteSource Of(WatchedSourceConfiguration source) => source.ApiParser switch
    {
        ApiParser.CongressHouseVoteList => VoteSource.CongressGov,
        ApiParser.VoteviewSenateVoteList => VoteSource.Voteview,
        _ => throw new ArgumentException($"Source '{source.Id}' is not a vote source.", nameof(source))
    };

    public static string RecordKeyOf(WatchedSourceConfiguration source) => Of(source) == VoteSource.CongressGov ? source.Id : SenateRecordKey;
}
