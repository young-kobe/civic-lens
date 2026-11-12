namespace CivicLens.Application.Publication;

/// <summary>The releases whose record states name one revision, from <see cref="First"/> to <see cref="Last"/>. Only their kept folders can hold its record file. A null last release means the range is open.</summary>
public sealed record KeptReleaseRange(int First, int? Last)
{
    public bool IsOpen => Last is null;

    public bool Covers(int releaseNumber) => First <= releaseNumber && (Last is not { } last || releaseNumber <= last);
}
