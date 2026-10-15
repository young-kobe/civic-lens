using System.Globalization;
using CivicLens.Application.Collection.Processing;

namespace CivicLens.Host.Collection;

internal static class EvidenceProcessingConfiguration
{
    private const string ConcurrencyVariable = "CIVIC_LENS_EVIDENCE_CONCURRENCY";

    internal static int ReadConcurrency()
    {
        var value = Environment.GetEnvironmentVariable(ConcurrencyVariable);
        if (string.IsNullOrWhiteSpace(value)) return EvidenceProcessingPolicy.DefaultConcurrency;
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            throw new ArgumentException($"{ConcurrencyVariable} must be a whole number.");
        return EvidenceProcessingPolicy.ValidateConcurrency(parsed);
    }
}
