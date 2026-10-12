using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace CivicLens.Application.Analysis;

public static class AnalysisPromptHash
{
    private static readonly JsonSerializerOptions CompactJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string Compute(string model, string effort, int maximumOutputTokens, string system, string user, string schema)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            model,
            effort,
            maxTokens = maximumOutputTokens,
            system,
            user,
            schema
        }, CompactJson);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
