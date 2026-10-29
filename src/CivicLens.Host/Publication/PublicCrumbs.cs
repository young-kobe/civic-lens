using CivicLens.Host.Components.Layout;
using CivicLens.Publication.Contracts;

namespace CivicLens.Host.Publication;

internal static class PublicCrumbs
{
    public static Crumb Home(string rootPath) => new("Home", rootPath + PublicationProtocol.HomePath);

    public static Crumb Section(PublicSection section, string rootPath)
    {
        var link = PublicSections.Of(section);
        return new(link.Label, rootPath + link.Path);
    }

    public static Crumb Current(PublicSection section) => new(PublicSections.Of(section).Label);
}
