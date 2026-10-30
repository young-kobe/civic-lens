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

    public static string HeadlineAndSummary(JsonObject? issueIds = null)
    {
        var properties = new JsonObject { ["headline"] = TextWithConfidence(), ["summary"] = TextWithConfidence() };
        var required = new JsonArray("headline", "summary");
        if (issueIds is not null)
        {
            properties["issueIds"] = issueIds;
            required.Add("issueIds");
        }
        var schema = new JsonObject { ["type"] = "object", ["additionalProperties"] = false, ["required"] = required, ["properties"] = properties };
        return schema.ToJsonString(AnalysisPromptText.CompactJson);
    }

    private static JsonObject TextWithConfidence() => new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new JsonArray("text", "confidence"),
        ["properties"] = new JsonObject { ["text"] = new JsonObject { ["type"] = "string" }, ["confidence"] = Number() }
    };
}
