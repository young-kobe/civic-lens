using CivicLens.Host.Components.Layout;
using CivicLens.Publication.Contracts;

namespace CivicLens.Host.Publication;

internal static class PublicCrumbs
{
    public static Crumb Home(string rootPath) => new("Home", rootPath + PublicationProtocol.HomePath);

    public static Crumb Officials(string rootPath) => new("Officials", rootPath + PublicationProtocol.OfficialsPagePath);

    public static Crumb Issues(string rootPath) => new("Issues", rootPath + PublicationProtocol.FacetsPath);

    public static Crumb Briefs(string rootPath) => new("Briefs", rootPath + PublicationProtocol.BriefsPagePath(1));

    public static Crumb AllRecords(string rootPath) => new("All records", rootPath + PublicationProtocol.AllRecordsPagePath(1));
}
