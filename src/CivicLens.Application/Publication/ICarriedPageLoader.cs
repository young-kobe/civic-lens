namespace CivicLens.Application.Publication;

/// <summary>A kind loader whose page lists related records, so it can render a carried record again.</summary>
internal interface ICarriedPageLoader
{
    Task<string> RenderCarriedAsync(string recordId, byte[] json, ReleaseLinks links, PublicationBuild build, CancellationToken cancellationToken);
}
