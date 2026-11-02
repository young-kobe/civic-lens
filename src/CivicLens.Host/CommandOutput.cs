using System.Text.Json;
using System.Text.Json.Serialization;

namespace CivicLens.Host;

internal static class CommandOutput
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static void Write(object value) => Write(Console.Out, value);

    public static void Write(TextWriter output, object value) => output.WriteLine(JsonSerializer.Serialize(value, JsonOptions));

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
