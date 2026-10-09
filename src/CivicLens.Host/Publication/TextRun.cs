namespace CivicLens.Host.Publication;

public sealed record TextRun(string Text, IReadOnlyList<RunMark> Marks);
