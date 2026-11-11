namespace CivicLens.Publication.Contracts;

/// <summary>Produces the one meaning and next-step pair for a published vote page from its captured evidence.</summary>
public static class VoteWording
{
    /// <summary>Returns the approved words, or null when the vote lacks a consistent cited question and result.</summary>
    public static (string Yes, string No, string NextStep)? Of(PublishedVote vote)
    {
        ArgumentNullException.ThrowIfNull(vote);
        return Of(VoteWordingSource.Of(vote));
    }

    /// <summary>Returns the approved words for the cited parts of a vote, or null when they lack a consistent cited question and result.</summary>
    public static (string Yes, string No, string NextStep)? Of(VoteWordingSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!VoteWordingInput.TryRead(source, out var input)) return null;

        var meaning = VoteMeaning.For(source.Kind, input);
        var nextStep = NextStep.For(source.Kind, source.Chamber, input);
        return (meaning.Yes, meaning.No, nextStep);
    }
}
