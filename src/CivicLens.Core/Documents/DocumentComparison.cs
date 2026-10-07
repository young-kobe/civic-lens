using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace CivicLens.Core.Documents;

public enum DocumentComparisonStatus { Complete, Incompatible, LimitExceeded }

/// <summary>A deterministic textual comparison of two immutable extractions. It makes no judgment about significance.</summary>
public sealed class DocumentComparison
{
    public const string CurrentAlgorithmVersion = "line-lcs-context-v1";
    public const string CurrentSettingsVersion = "bounded-lines-2000-cells-1000000-change-8192-context-1-word-tokens-512-cells-250000-total-1000000-v1";
    public const int MaximumLines = 2_000;
    public const int MaximumMatrixCells = 1_000_000;
    public const int MaximumChangedTextLength = 8_192;
    public const int ContextLines = 1;
    public const int MaximumContextTextLength = 8_192;
    public const int MaximumWordTokens = 512;
    public const int MaximumWordMatrixCells = 250_000;
    public const int MaximumTotalWordMatrixCells = 1_000_000;

    private DocumentComparison(string id, string beforeId, string afterId, DocumentComparisonStatus status,
        string? reason, ImmutableArray<DocumentComparisonHunk> hunks)
    {
        ComparisonId = id;
        AlgorithmVersion = CurrentAlgorithmVersion;
        SettingsVersion = CurrentSettingsVersion;
        BeforeExtractionId = beforeId;
        AfterExtractionId = afterId;
        Status = status;
        Reason = reason;
        Hunks = hunks;
    }

    public string ComparisonId { get; }
    public string AlgorithmVersion { get; }
    public string SettingsVersion { get; }
    public string BeforeExtractionId { get; }
    public string AfterExtractionId { get; }
    public DocumentComparisonStatus Status { get; }
    public string? Reason { get; }
    public ImmutableArray<DocumentComparisonHunk> Hunks { get; }

    public static DocumentComparison Create(DocumentExtraction before, DocumentExtraction after,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        cancellationToken.ThrowIfCancellationRequested();
        var id = CreateId(before.ExtractionId, after.ExtractionId);
        if (!IsCompatible(before, after))
            return new(id, before.ExtractionId, after.ExtractionId, DocumentComparisonStatus.Incompatible, "The extractions do not share source, requested URL, parser, normalization, and profile settings.", []);

        var left = SplitLines(before.Text);
        var right = SplitLines(after.Text);
        if (left.Length > MaximumLines || right.Length > MaximumLines || (long)(left.Length + 1) * (right.Length + 1) > MaximumMatrixCells)
            return new(id, before.ExtractionId, after.ExtractionId, DocumentComparisonStatus.LimitExceeded, "The extraction exceeds the configured line or comparison-cell limit.", []);

        var width = right.Length + 1;
        var table = new int[(left.Length + 1) * width];
        for (var i = left.Length - 1; i >= 0; i--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var j = right.Length - 1; j >= 0; j--)
                table[i * width + j] = left[i].Text == right[j].Text
                    ? table[(i + 1) * width + j + 1] + 1
                    : Math.Max(table[(i + 1) * width + j], table[i * width + j + 1]);
        }

        var hunks = BuildHunks(left, right, table, width, cancellationToken);
        if (hunks is null)
            return new(id, before.ExtractionId, after.ExtractionId, DocumentComparisonStatus.LimitExceeded, "A changed block exceeds the configured detail limit.", []);
        return new(id, before.ExtractionId, after.ExtractionId, DocumentComparisonStatus.Complete, null, hunks.Value);
    }

    private static bool IsCompatible(DocumentExtraction before, DocumentExtraction after) =>
        before.SourceAttempt.SourceId == after.SourceAttempt.SourceId &&
        before.SourceAttempt.RequestedUrl == after.SourceAttempt.RequestedUrl &&
        before.ParserVersion == after.ParserVersion && before.NormalizationVersion == after.NormalizationVersion &&
        ((before.Profile is null && after.Profile is null) || before.Profile?.Matches(after.Profile) == true);

    private static ImmutableArray<Line> SplitLines(string text)
    {
        var lines = ImmutableArray.CreateBuilder<Line>();
        var start = 0;
        for (var index = 0; index < text.Length; index++)
            if (text[index] == '\n')
            {
                lines.Add(new(start, index + 1 - start, text.Substring(start, index + 1 - start)));
                start = index + 1;
                if (lines.Count > MaximumLines) return lines.ToImmutable();
            }
        if (start < text.Length || text.Length == 0)
            lines.Add(new(start, text.Length - start, text[start..]));
        return lines.ToImmutable();
    }

    private static ImmutableArray<DocumentComparisonHunk>? BuildHunks(ImmutableArray<Line> left,
        ImmutableArray<Line> right, int[] table, int width, CancellationToken cancellationToken)
    {
        var output = ImmutableArray.CreateBuilder<DocumentComparisonHunk>();
        var remainingWordCells = MaximumTotalWordMatrixCells;
        var i = 0; var j = 0;
        var beforeStart = -1; var afterStart = -1;
        while (i < left.Length || j < right.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var equal = i < left.Length && j < right.Length && left[i].Text == right[j].Text;
            if (equal)
            {
                if (beforeStart >= 0)
                {
                    if (!AddHunk(output, left, right, beforeStart, i, afterStart, j, ref remainingWordCells, cancellationToken)) return null;
                    beforeStart = afterStart = -1;
                }
                i++; j++; continue;
            }
            if (beforeStart < 0) { beforeStart = i; afterStart = j; }
            if (j == right.Length || (i < left.Length && table[(i + 1) * width + j] >= table[i * width + j + 1]))
            {
                i++;
            }
            else
            {
                j++;
            }
        }
        if (beforeStart >= 0 && !AddHunk(output, left, right, beforeStart, i, afterStart, j, ref remainingWordCells, cancellationToken)) return null;
        return output.ToImmutable();
    }

    private static bool AddHunk(ImmutableArray<DocumentComparisonHunk>.Builder output, ImmutableArray<Line> left,
        ImmutableArray<Line> right, int beforeFirst, int beforePast, int afterFirst, int afterPast,
        ref int remainingWordCells, CancellationToken cancellationToken)
    {
        var beforeOffset = beforeFirst < left.Length ? left[beforeFirst].Start : (left.Length == 0 ? 0 : left[^1].Start + left[^1].Length);
        var afterOffset = afterFirst < right.Length ? right[afterFirst].Start : (right.Length == 0 ? 0 : right[^1].Start + right[^1].Length);
        var beforeText = string.Concat(left.Skip(beforeFirst).Take(beforePast - beforeFirst).Select(line => line.Text));
        var afterText = string.Concat(right.Skip(afterFirst).Take(afterPast - afterFirst).Select(line => line.Text));
        if (beforeText.Length + afterText.Length > MaximumChangedTextLength) return false;
        var wordEdits = CreateWordEdits(beforeText, afterText, beforeOffset, afterOffset, ref remainingWordCells, cancellationToken);
        if (wordEdits is null) return false;
        var beforeContext = Context(left, beforeFirst, beforePast);
        var afterContext = Context(right, afterFirst, afterPast);
        if (beforeContext.Length > MaximumContextTextLength || afterContext.Length > MaximumContextTextLength) return false;
        output.Add(new(beforeOffset, beforeText.Length, afterOffset, afterText.Length, beforeText, afterText,
            ContextStart(left, beforeFirst), beforeContext, ContextStart(right, afterFirst), afterContext, wordEdits.Value));
        return true;
    }

    private static ImmutableArray<DocumentWordEdit>? CreateWordEdits(string before, string after, int beforeOffset, int afterOffset, ref int remainingWordCells, CancellationToken cancellationToken)
    {
        var left = Words(before);
        var right = Words(after);
        if (left.Count > MaximumWordTokens || right.Count > MaximumWordTokens ||
            (long)(left.Count + 1) * (right.Count + 1) > MaximumWordMatrixCells) return null;
        var cells = (left.Count + 1) * (right.Count + 1);
        if (cells > remainingWordCells) return null;
        remainingWordCells -= cells;
        var width = right.Count + 1;
        var table = new int[(left.Count + 1) * width];
        for (var i = left.Count - 1; i >= 0; i--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var j = right.Count - 1; j >= 0; j--)
                table[i * width + j] = left[i].Value == right[j].Value ? table[(i + 1) * width + j + 1] + 1 :
                    Math.Max(table[(i + 1) * width + j], table[i * width + j + 1]);
        }
        var edits = ImmutableArray.CreateBuilder<DocumentWordEdit>();
        var iLeft = 0;
        var iRight = 0;
        var editLeftStart = -1;
        var editRightStart = -1;
        while (iLeft < left.Count || iRight < right.Count)
        {
            var equal = iLeft < left.Count && iRight < right.Count && left[iLeft].Value == right[iRight].Value;
            if (equal)
            {
                AddEdit();
                iLeft++; iRight++;
                continue;
            }
            if (editLeftStart < 0) { editLeftStart = iLeft; editRightStart = iRight; }
            if (iRight == right.Count || (iLeft < left.Count && table[(iLeft + 1) * width + iRight] >= table[iLeft * width + iRight + 1])) iLeft++;
            else iRight++;
        }
        AddEdit();
        return edits.ToImmutable();

        void AddEdit()
        {
            if (editLeftStart < 0) return;
            var beforeStart = editLeftStart < left.Count ? left[editLeftStart].Start : before.Length;
            var afterStart = editRightStart < right.Count ? right[editRightStart].Start : after.Length;
            var beforeEnd = iLeft > editLeftStart ? left[iLeft - 1].Start + left[iLeft - 1].Length : beforeStart;
            var afterEnd = iRight > editRightStart ? right[iRight - 1].Start + right[iRight - 1].Length : afterStart;
            edits.Add(new(beforeOffset + beforeStart, beforeEnd - beforeStart, afterOffset + afterStart, afterEnd - afterStart));
            editLeftStart = editRightStart = -1;
        }
    }

    private static List<Word> Words(string text)
    {
        var words = new List<Word>();
        var start = 0;
        while (start < text.Length)
        {
            var whitespace = char.IsWhiteSpace(text[start]);
            var end = start + 1;
            while (end < text.Length && char.IsWhiteSpace(text[end]) == whitespace) end++;
            words.Add(new(text[start..end], start, end - start));
            start = end;
        }
        return words;
    }

    private static string Context(ImmutableArray<Line> lines, int first, int past)
    {
        var start = Math.Max(0, first - ContextLines);
        var end = Math.Min(lines.Length, past + ContextLines);
        return string.Concat(lines.Skip(start).Take(end - start).Select(line => line.Text));
    }

    private static int ContextStart(ImmutableArray<Line> lines, int first) =>
        lines[Math.Max(0, first - ContextLines)].Start;

    private static string CreateId(string before, string after) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{CurrentAlgorithmVersion}\0{CurrentSettingsVersion}\0{before}\0{after}")));

    private sealed record Line(int Start, int Length, string Text);
    private sealed record Word(string Value, int Start, int Length);
}
