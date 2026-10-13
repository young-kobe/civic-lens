using System.Text.Json;
using System.Text.Json.Serialization;

namespace CivicLens.Publication.Contracts;

public sealed class PublishedEnumJsonConverter : JsonStringEnumConverter
{
    public PublishedEnumJsonConverter() : base(JsonNamingPolicy.CamelCase, allowIntegerValues: false)
    {
    }
}
