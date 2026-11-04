using System.Text.Json;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>Reads a record file, validates it, and builds the manifest entry that the record yields.</summary>
internal static class RecordFileEntry
{
    public static PublishedRecordEntry Read(PublishedRecordKind kind, byte[] json) => kind switch
    {
        PublishedRecordKind.DocumentChange => Of<PublishedDocumentChange>(json, record => { record.Validate(); return record.ToEntry(); }),
        PublishedRecordKind.Statement => Of<PublishedStatement>(json, record => { record.Validate(); return record.ToEntry(); }),
        PublishedRecordKind.Action => Of<PublishedAction>(json, record => { record.Validate(); return record.ToEntry(); }),
        PublishedRecordKind.Brief => Of<PublishedBrief>(json, record => { record.Validate(); return record.ToEntry(); }),
        PublishedRecordKind.Vote => Of<PublishedVote>(json, record => { record.Validate(); return record.ToEntry(); }),
        PublishedRecordKind.Relationship => Of<PublishedRelationship>(json, record => { record.Validate(); return record.ToEntry(); }),
        PublishedRecordKind.ExecutiveAction => Of<PublishedExecutiveAction>(json, record => { record.Validate(); return record.ToEntry(); }),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Record kind has no record file.")
    };

    private static PublishedRecordEntry Of<TRecord>(byte[] json, Func<TRecord, PublishedRecordEntry> toEntry) where TRecord : class =>
        toEntry(JsonSerializer.Deserialize<TRecord>(json, PublicationProtocol.JsonOptions) ?? throw new InvalidDataException("A record file is empty."));
}
