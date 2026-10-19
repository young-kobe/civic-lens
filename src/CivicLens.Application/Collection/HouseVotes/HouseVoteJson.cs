using System.Text.Json;

namespace CivicLens.Application.Collection.HouseVotes;

internal static class HouseVoteJson
{
    public static T Deserialize<T>(ReadOnlySpan<byte> json, string name)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, CongressApiJson.Options)
                ?? throw new InvalidDataException($"{name} is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"{name} is not valid JSON of the expected shape.", exception);
        }
    }
}
