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

    public static string HeadlineAndSummary(JsonObject? issueIds = null, bool withDecides = false)
    {
        var properties = new JsonObject { ["headline"] = TextWithConfidence(), ["summary"] = TextWithConfidence() };
        var required = new JsonArray("headline", "summary");
        if (withDecides)
        {
            properties["decides"] = QuotedTextWithConfidence();
            required.Add("decides");
        }
        if (issueIds is not null)
        {
            properties["issueIds"] = issueIds;
            required.Add("issueIds");
        }
        var schema = new JsonObject { ["type"] = "object", ["additionalProperties"] = false, ["required"] = required, ["properties"] = properties };
        return schema.ToJsonString(AnalysisPromptText.CompactJson);
    }

    public static string BillTextDraft(JsonObject? issueIds = null)
    {
        var properties = new JsonObject { ["headline"] = TextWithConfidence(), ["summary"] = SentencesWithConfidence(), ["whyItMatters"] = SentencesWithConfidence() };
        var required = new JsonArray("headline", "summary", "whyItMatters");
        if (issueIds is not null)
        {
            properties["issueIds"] = issueIds;
            required.Add("issueIds");
        }
        var schema = new JsonObject { ["type"] = "object", ["additionalProperties"] = false, ["required"] = required, ["properties"] = properties };
        return schema.ToJsonString(AnalysisPromptText.CompactJson);
    }

    public static string IssueChoice(JsonObject issueIds)
    {
        var properties = new JsonObject { ["issueIds"] = issueIds, ["confidence"] = Number() };
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("issueIds", "confidence"),
            ["properties"] = properties
        };
        return schema.ToJsonString(AnalysisPromptText.CompactJson);
    }

    private static JsonObject QuotedSentence() => new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new JsonArray("sentence", "quote"),
        ["properties"] = new JsonObject { ["sentence"] = new JsonObject { ["type"] = "string" }, ["quote"] = new JsonObject { ["type"] = "string" } }
    };

    private static JsonObject SentencesWithConfidence() => new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new JsonArray("sentences", "confidence"),
        ["properties"] = new JsonObject
        {
            ["sentences"] = new JsonObject { ["type"] = "array", ["items"] = QuotedSentence() },
            ["confidence"] = Number()
        }
    };

    private static JsonObject QuotedTextWithConfidence() => new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new JsonArray("text", "quote", "confidence"),
        ["properties"] = new JsonObject
        {
            ["text"] = new JsonObject { ["type"] = "string" },
            ["quote"] = new JsonObject { ["type"] = "string" },
            ["confidence"] = Number()
        }
    };

    private static JsonObject TextWithConfidence() => new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new JsonArray("text", "confidence"),
        ["properties"] = new JsonObject { ["text"] = new JsonObject { ["type"] = "string" }, ["confidence"] = Number() }
    };
}
