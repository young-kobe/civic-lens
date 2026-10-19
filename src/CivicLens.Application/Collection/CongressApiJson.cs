using System.Text.Json;
using System.Text.Json.Serialization;

namespace CivicLens.Application.Collection;

internal static class CongressApiJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };
}
