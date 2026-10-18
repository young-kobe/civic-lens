namespace CivicLens.Application.Publication;

/// <summary>A kind loader whose page lists related records, so it can render a carried record again.</summary>
internal interface ICarriedPageLoader
{
    Task<IReadOnlyDictionary<string, string>> RenderCarriedAsync(IReadOnlyDictionary<string, byte[]> records, ReleaseLinks links,
        PublicationBuild build, CancellationToken cancellationToken);
}
