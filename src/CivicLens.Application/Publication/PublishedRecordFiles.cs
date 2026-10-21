using System.Text.Json;
using System.Text.Json.Serialization;
using CivicLens.Core.Documents;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

public static class PublishedRecordFiles
{
    private const int MaximumBytesPerCharacter = 6;
    private const int CitationAllowance = 16 * 1024 * 1024;

    // An action embeds the most sources. JSON escapes one character into at most six bytes.
    internal static readonly JsonSerializerOptions PartialReadOptions =
        new(PublicationProtocol.JsonOptions) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip, RespectRequiredConstructorParameters = true };

    public const int MaximumCitedRecordBytes =
        PublishedAction.MaximumSources * DocumentExtraction.MaximumTextLength * MaximumBytesPerCharacter + CitationAllowance;
}
