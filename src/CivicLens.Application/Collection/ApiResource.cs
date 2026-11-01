using System.Text.Json;
using System.Text.Json.Serialization;

namespace CivicLens.Application.Collection;

[JsonConverter(typeof(ApiResourceJsonConverter))]
public enum ApiResource
{
    BillList,
    Bill,
    BillCosponsors,
    BillActions,
    BillSummaries,
    BillSubjects,
    BillTextVersions,
    BillText,
    HouseVoteList,
    HouseVoteDetail,
    HouseVoteMembers,
    /// <summary>Retired: stored job definitions still name it. Preparation blocks such a job.</summary>
    SenateVoteMenu,
    SenateVote,
    VoteviewRollCalls,
    VoteviewVotes,
    VoteviewMembers,
    MemberRoster,
    ExecutiveRoster,
    MemberPhoto,
    FederalRegisterList,
    FederalRegisterDocument
}

public sealed class ApiResourceJsonConverter : JsonStringEnumConverter<ApiResource>
{
    public ApiResourceJsonConverter() : base(JsonNamingPolicy.CamelCase, allowIntegerValues: false)
    {
    }
}
