using System.Text.Json.Serialization;

namespace CivicLens.Publication.Contracts;

/// <summary>
/// Who rewrote the headline or the summary in a code update that changed nothing else. None is a source update. A person rewrite always has a note, so it
/// has no value here. The values are fixed: the correction history identity writes them as one byte, and None and Model are the bytes of the earlier bool.
/// </summary>
[JsonConverter(typeof(PublishedEnumJsonConverter))]
public enum PublishedTextRewrite : byte
{
    None = 0,
    Model = 1,
    Code = 2
}
