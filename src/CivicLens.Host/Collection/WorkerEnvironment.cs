using System.Globalization;

namespace CivicLens.Host.Collection;

internal static class WorkerEnvironment
{
    internal static int? ReadWholeNumber(string variable)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(value)) return null;
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new ArgumentException($"{variable} must be a whole number.");
    }
}
