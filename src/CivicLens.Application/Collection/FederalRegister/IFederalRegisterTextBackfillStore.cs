namespace CivicLens.Application.Collection.FederalRegister;

/// <summary>An original document with no text row and no text job.</summary>
public sealed record FederalRegisterTextTarget(string DocumentNumber, DateOnly PublishedOn);

public interface IFederalRegisterTextBackfillStore
{
    /// <summary>The newest original documents first. A document with a text row or a text job is not listed.</summary>
    Task<IReadOnlyList<FederalRegisterTextTarget>> ListNewestOriginalsWithoutTextAsync(ConfiguredCollectionSource textSource, int limit,
        CancellationToken cancellationToken);

    /// <summary>Returns false when the text job of the document exists.</summary>
    Task<bool> AdmitTextJobAsync(ConfiguredCollectionSource textSource, FederalRegisterTextTarget target, CancellationToken cancellationToken);
}
