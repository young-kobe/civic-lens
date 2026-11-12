using System.Text.Json.Serialization;

namespace CivicLens.Publication.Contracts;

/// <summary>
/// The bill text behind a summary that an AI model wrote from the text. The sentences come in two parts, 2 or 3 of the summary and 1 to 3 of why it matters.
/// The counts are the characters of one bill text version: those that the model read, and all of them.
/// </summary>
public sealed record PublishedBillText
{
    public const int MaximumVersionNameLength = 128;
    public const int MinimumSummarySentences = 2;
    public const int MaximumSummarySentences = 3;
    public const int MaximumWhyItMattersSentences = 3;

    /// <summary>The name that Congress.gov gives the version, such as "Introduced in House".</summary>
    public required string VersionName { get; init; }
    public DateOnly? VersionDate { get; init; }
    public required int CharsRead { get; init; }
    public required int CharsTotal { get; init; }
    public required PublishedTextSentence[] Sentences { get; init; }

    /// <summary>The key terms that the model chose, each defined by an exact quote of the definitions section. Absent when the bill has none.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PublishedKeyTerm[]? KeyTerms { get; init; }

    public IEnumerable<PublishedCitation> Citations() =>
        Sentences.Select(item => item.Citation).Concat(KeyTerms?.Select(item => item.Citation) ?? []);

    public IEnumerable<PublishedTextSentence> In(PublishedTextPart part) => Sentences.Where(item => item.Part == part);

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(VersionName) || VersionName.Length > MaximumVersionNameLength)
            throw new InvalidDataException("Published bill text requires a version name.");
        if (CharsRead < 1 || CharsRead > CharsTotal)
            throw new InvalidDataException("Published bill text counts 1 or more characters read, and no more than the total.");
        if (Sentences is null || Sentences.Any(item => item is null || string.IsNullOrWhiteSpace(item.Sentence) || !Enum.IsDefined(item.Part)))
            throw new InvalidDataException("Published bill text sentences need text and a known part.");
        var summary = In(PublishedTextPart.Summary).Count();
        var why = In(PublishedTextPart.WhyItMatters).Count();
        var summaryComesFirst = Sentences.Take(summary).All(item => item.Part == PublishedTextPart.Summary);
        if (summary is < MinimumSummarySentences or > MaximumSummarySentences || why is < 1 or > MaximumWhyItMattersSentences || !summaryComesFirst)
            throw new InvalidDataException("Published bill text gives 2 or 3 summary sentences, then 1 to 3 why it matters sentences.");
        ValidateKeyTerms();
    }

    private void ValidateKeyTerms()
    {
        if (KeyTerms is null) return;
        if (KeyTerms.Length is 0 or > PublishedKeyTerm.MaximumTerms || KeyTerms.Any(item => item is null ||
                string.IsNullOrWhiteSpace(item.Term) || item.Term.Length > PublishedKeyTerm.MaximumTermLength || item.Citation is null))
            throw new InvalidDataException($"Published key terms are absent or number 1 to {PublishedKeyTerm.MaximumTerms}, each with a term and a citation.");
        if (KeyTerms.Select(item => item.Term).Distinct(StringComparer.OrdinalIgnoreCase).Count() != KeyTerms.Length)
            throw new InvalidDataException("A published key term appears once.");
    }
}
