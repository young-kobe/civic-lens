using CivicLens.Application.Collection.Discovery;

namespace CivicLens.Application.Collection.FederalRegister;

public interface IFederalRegisterAdmissionStore
{
    Task<DiscoveryAdmissionResult> AdmitListPageAsync(ConfiguredCollectionSource source, FederalRegisterListPage page,
        CancellationToken cancellationToken);

    /// <summary>Records the document as a revision of its record and approves it by code, in one transaction.</summary>
    Task<DiscoveryAdmissionResult> AdmitDocumentAsync(ConfiguredCollectionSource source, string documentJobId, FederalRegisterEvidence evidence,
        CancellationToken cancellationToken);

    /// <summary>Fills a null <c>body_html_url</c> of the stored document and changes no other column. A pinned URL stays.</summary>
    Task AdmitBodyUrlAsync(ConfiguredCollectionSource source, string documentNumber, string? bodyUrl, CancellationToken cancellationToken);

    /// <summary>Saves the text of one original document. A repeat adds no second row.</summary>
    Task AdmitTextAsync(ConfiguredCollectionSource textSource, string textJobId, FederalRegisterTextEvidence evidence,
        CancellationToken cancellationToken);
}
