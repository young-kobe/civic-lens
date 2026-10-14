using System.Text.Json.Serialization;

namespace CivicLens.Application.Collection;

public sealed record PacingGroupConfiguration
{
    public const int MaximumDelayMilliseconds = 3_600_000;

    public required string HostSuffix { get; init; }
    public required int MinDelayMilliseconds { get; init; }

    [JsonIgnore]
    public string Key => "group:" + HostSuffix;

    public bool Matches(string host) =>
        host.Equals(HostSuffix, StringComparison.Ordinal) || host.EndsWith("." + HostSuffix, StringComparison.Ordinal);

    public void Validate()
    {
        if (!IsHostSuffix(HostSuffix))
            throw new ArgumentException("A pacing group host suffix must be a lowercase DNS name with at least two labels.");
        if (MinDelayMilliseconds is < 1 or > MaximumDelayMilliseconds)
            throw new ArgumentException($"Pacing group '{HostSuffix}' delay must be from 1 to {MaximumDelayMilliseconds} milliseconds.");
    }

    private static bool IsHostSuffix(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 253) return false;
        var labels = value.Split('.');
        return labels.Length >= 2 && labels.All(IsLabel);
    }

    private static bool IsLabel(string label) =>
        label.Length is >= 1 and <= 63 && label[0] != '-' && label[^1] != '-' &&
        label.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');
}
