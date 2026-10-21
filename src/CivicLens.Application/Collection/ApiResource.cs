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
    HouseVoteList,
    HouseVoteDetail,
    HouseVoteMembers,
    SenateVoteMenu,
    SenateVote,
    MemberRoster,
    ExecutiveRoster
}

public sealed class ApiResourceJsonConverter : JsonStringEnumConverter<ApiResource>
{
    public ApiResourceJsonConverter() : base(JsonNamingPolicy.CamelCase, allowIntegerValues: false)
    {
    }
}
