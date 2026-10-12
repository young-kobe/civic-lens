using System.Text.Json.Nodes;

namespace CivicLens.Application.Analysis;

public static class AnalysisSchema
{
    public static JsonObject Number() => new() { ["type"] = "number" };

    public static JsonObject NullableString() => new()
    {
        ["anyOf"] = new JsonArray(new JsonObject { ["type"] = "string" }, new JsonObject { ["type"] = "null" })
    };

    public static JsonObject IdString(IEnumerable<string> ids)
    {
        var values = ids.ToArray();
        var id = new JsonObject { ["type"] = "string" };
        if (values.Length > 0) id["enum"] = new JsonArray([.. values.Select(value => (JsonNode)value)]);
        return id;
    }
}
