using System.Text.Json;
using System.Text.Json.Serialization;

namespace CivicLens.Application.Collection;

[JsonConverter(typeof(ApiResourceJsonConverter))]
public enum ApiResource
{
    BillList,
    Bill
}

public sealed class ApiResourceJsonConverter : JsonStringEnumConverter<ApiResource>
{
    public ApiResourceJsonConverter() : base(JsonNamingPolicy.CamelCase, allowIntegerValues: false)
    {
    }
}
