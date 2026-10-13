namespace CivicLens.Core;

public static class LowercaseHex
{
    public static bool Is(string? value, int length) =>
        value is not null && value.Length == length && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
