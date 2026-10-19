using System.Text.Json;
using System.Text.Json.Serialization;

namespace CivicLens.Application.Collection;

internal static class CongressApiJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public static T Deserialize<T>(ReadOnlySpan<byte> json, string name)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, Options)
                ?? throw new InvalidDataException($"{name} is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"{name} is not valid JSON of the expected shape.", exception);
        }
    }
}
