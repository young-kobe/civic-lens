using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.DocumentChanges;

public sealed record DocumentChangeDetail(int Release, DocumentChangeRow DocumentChange, PublishedDocumentVersion Before,
    PublishedDocumentVersion After, PublishedChange[] Changes, PublishedCitation[] Citations, PublishedCitation? ChangeDateEvidence);
