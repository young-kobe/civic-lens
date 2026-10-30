using System.Xml;
using System.Xml.Linq;

namespace CivicLens.Application.Collection;

internal static class BoundedXml
{
    private const long MaximumCharacters = 10_000_000;

    public static XElement LoadRoot(ReadOnlySpan<byte> xml, string name, string rootName)
    {
        try
        {
            using var stream = new MemoryStream(xml.ToArray());
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaximumCharacters
            };
            using var reader = XmlReader.Create(stream, settings);
            var root = XDocument.Load(reader).Root;
            return root is not null && root.Name.LocalName == rootName
                ? root
                : throw new InvalidDataException($"{name} must have a {rootName} root.");
        }
        catch (XmlException exception)
        {
            throw new InvalidDataException($"{name} is not valid XML.", exception);
        }
    }
}
