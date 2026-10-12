using System.Collections.Immutable;

namespace CivicLens.Core.Review;

public static class DocumentChangeRevisionComparison
{
    public static ImmutableArray<DocumentChangeField> ChangedFields(DocumentChangeDraftRevision original,
        DocumentChangeDraftRevision current)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(current);
        var changed = ImmutableArray.CreateBuilder<DocumentChangeField>();
        Add(changed, DocumentChangeField.Headline, original.Headline != current.Headline);
        Add(changed, DocumentChangeField.Summary, original.Summary != current.Summary);
        Add(changed, DocumentChangeField.Significance, original.Significance != current.Significance);
        Add(changed, DocumentChangeField.Limits, original.Limits != current.Limits);
        Add(changed, DocumentChangeField.Institution, original.Institution != current.Institution);
        Add(changed, DocumentChangeField.ChangeDate,
            original.ChangeDate != current.ChangeDate || original.ChangeDateEvidence != current.ChangeDateEvidence);
        Add(changed, DocumentChangeField.OfficialIds, !SameSet(original.OfficialIds, current.OfficialIds));
        Add(changed, DocumentChangeField.IssueIds, !SameSet(original.IssueIds, current.IssueIds));
        Add(changed, DocumentChangeField.Citations, !SameSet(original.Citations, current.Citations));
        return changed.ToImmutable();
    }

    private static void Add(ImmutableArray<DocumentChangeField>.Builder changed, DocumentChangeField field, bool isChanged)
    {
        if (isChanged) changed.Add(field);
    }

    private static bool SameSet<T>(ImmutableArray<T> first, ImmutableArray<T> second) =>
        first.ToHashSet().SetEquals(second);
}
