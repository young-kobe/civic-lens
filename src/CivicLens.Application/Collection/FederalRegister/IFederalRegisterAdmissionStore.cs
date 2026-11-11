using CivicLens.Application.Collection.Discovery;

namespace CivicLens.Application.Collection.FederalRegister;

public interface IFederalRegisterAdmissionStore
{
    Task<DiscoveryAdmissionResult> AdmitListPageAsync(ConfiguredCollectionSource source, FederalRegisterListPage page,
        CancellationToken cancellationToken);

    /// <summary>Records the document as a revision of its record and approves it by code, in one transaction.</summary>
    Task<DiscoveryAdmissionResult> AdmitDocumentAsync(ConfiguredCollectionSource source, string documentJobId, FederalRegisterEvidence evidence,
        CancellationToken cancellationToken);

    /// <summary>Saves the text of one original document. A repeat adds no second row.</summary>
    Task AdmitTextAsync(ConfiguredCollectionSource textSource, string textJobId, FederalRegisterTextEvidence evidence,
        CancellationToken cancellationToken);
}
