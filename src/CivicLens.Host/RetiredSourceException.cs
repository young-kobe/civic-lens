namespace CivicLens.Host;

internal sealed class RetiredSourceException(string sourceId)
    : ArgumentException($"Source '{sourceId}' uses the retired senate.gov parser. Set \"enabled\":false on it or remove it.");
