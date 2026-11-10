namespace CivicLens.Publication.Contracts;

/// <summary>Produces the one meaning and next-step pair for a published vote page from its captured evidence.</summary>
public static class VoteWording
{
    /// <summary>Returns the approved words, or null when the vote lacks a consistent cited question and result.</summary>
    public static (string Yes, string No, string NextStep)? Of(PublishedVote vote)
    {
        ArgumentNullException.ThrowIfNull(vote);
        if (!VoteWordingInput.TryRead(vote, out var input)) return null;

        var meaning = VoteMeaning.For(vote, input);
        var nextStep = NextStep.For(vote, input);
        return (meaning.Yes, meaning.No, nextStep);
    }
}
