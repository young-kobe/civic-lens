using System.Text.Json.Serialization;

namespace CivicLens.Core.ExecutiveActions;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ExecutiveActionKind { ExecutiveOrder, Proclamation, Notice, Memorandum }

public static class ExecutiveActionKinds
{
    public static string Label(this ExecutiveActionKind kind) => kind switch
    {
        ExecutiveActionKind.ExecutiveOrder => "Executive Order",
        ExecutiveActionKind.Proclamation => "Proclamation",
        ExecutiveActionKind.Notice => "Notice",
        ExecutiveActionKind.Memorandum => "Memorandum",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Executive action kind has no label.")
    };

    public static ExecutiveActionKind? TryRead(string? label) =>
        Enum.GetValues<ExecutiveActionKind>().Select(kind => (ExecutiveActionKind?)kind).FirstOrDefault(kind => kind!.Value.Label() == label);
}
