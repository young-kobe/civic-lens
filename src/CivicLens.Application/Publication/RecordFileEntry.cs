using System.Text.Json;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>Reads a record file, validates it, and builds the manifest entry that the record yields.</summary>
internal static class RecordFileEntry
{
    public static PublishedRecordEntry Read(PublishedRecordKind kind, byte[] json) => ReadContent(kind, json).Entry;

    public static (PublishedRecordEntry Entry, PublishedCorrection[] Corrections) ReadContent(PublishedRecordKind kind, byte[] json) => kind switch
    {
        PublishedRecordKind.DocumentChange => Of<PublishedDocumentChange>(json, record => { record.Validate(); return (record.ToEntry(), record.Corrections); }),
        PublishedRecordKind.Statement => Of<PublishedStatement>(json, record => { record.Validate(); return (record.ToEntry(), record.Corrections); }),
        PublishedRecordKind.Action => Of<PublishedAction>(json, record => { record.Validate(); return (BillLine.EntryOf(record), record.Corrections); }),
        PublishedRecordKind.Brief => Of<PublishedBrief>(json, record => { record.Validate(); return (record.ToEntry(), record.Corrections); }),
        PublishedRecordKind.Vote => Of<PublishedVote>(json, record => { record.Validate(); return (record.ToEntry(), record.Corrections); }),
        PublishedRecordKind.Relationship => Of<PublishedRelationship>(json, record => { record.Validate(); return (record.ToEntry(), record.Corrections); }),
        PublishedRecordKind.ExecutiveAction => Of<PublishedExecutiveAction>(json, record => { record.Validate(); return (record.ToEntry(), record.Corrections); }),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Record kind has no record file.")
    };

    private static (PublishedRecordEntry Entry, PublishedCorrection[] Corrections) Of<TRecord>(byte[] json,
        Func<TRecord, (PublishedRecordEntry Entry, PublishedCorrection[] Corrections)> toEntry) where TRecord : class
    {
        try
        {
            return toEntry(ReleaseRecordFiles.Deserialize<TRecord>(json));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("A published record file cannot be read.", exception);
        }
    }
}
