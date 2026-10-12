using System.Collections.Immutable;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CivicLens.Application.Analysis;

public static class AnalysisPromptText
{
    public static readonly JsonSerializerOptions EvidenceJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static readonly JsonSerializerOptions CompactJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string WithRejectionReasons(StringBuilder prompt, ImmutableArray<string> previousErrors)
    {
        if (previousErrors.IsDefaultOrEmpty) return prompt.ToString();
        prompt.Append("\n\nYour previous answer was rejected for these reasons. Write a new answer that corrects them:\n");
        foreach (var error in previousErrors) prompt.Append("- ").Append(error).Append('\n');
        return prompt.ToString();
    }
}
