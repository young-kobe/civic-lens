using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Html;

namespace CivicLens.Host.Components.Evidence;

/// <summary>Encodes markup while preserving code points that HTML normalizes or remaps.</summary>
public sealed class EncodedEvidence(string value) : IHtmlContent
{
    public void WriteTo(TextWriter writer, HtmlEncoder encoder)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(encoder);
        var start = 0;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character != '\r' && character is not (>= '\u0080' and <= '\u009f')) continue;
            encoder.Encode(writer, value, start, index - start);
            // Literal CR is normalized; C1 numeric references are remapped by HTML to Windows-1252.
            // Literal C1 characters contain no HTML syntax and retain their original code points.
            if (character == '\r') writer.Write("&#13;");
            else writer.Write(character);
            start = index + 1;
        }
        encoder.Encode(writer, value, start, value.Length - start);
    }

    public static EncodedEvidence From(string value) => new(value);

    public static MarkupString Markup(string value)
    {
        using var writer = new StringWriter();
        From(value).WriteTo(writer, HtmlEncoder.Default);
        return new MarkupString(writer.ToString());
    }
}
