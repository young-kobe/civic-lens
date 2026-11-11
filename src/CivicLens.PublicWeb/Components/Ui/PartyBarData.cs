using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Components.Ui;

/// <summary>The casts of one group on one roll call. The group is one party or a whole chamber. Every count is known and covers every member of the group.</summary>
public sealed record PartyBarData(string Name, PartyTone Tone, int Yes, int No, int Present, int NotVoting, int Other)
{
    public int Total => Yes + No + Present + NotVoting + Other;

    /// <summary>The smallest count of Yes that is more than half of the Yes and No casts. Null when nobody cast Yes or No.</summary>
    public int? Majority => Yes + No == 0 ? null : (Yes + No) / 2 + 1;

    public string Description =>
        $"{Name}: {PlainVoteWords.Yes} {Yes}, {PlainVoteWords.No} {No}, {PlainVoteWords.Present} {Present}, {PlainVoteWords.NotVoting} {NotVoting}" + (Other > 0 ? $", {OtherWord} {Other}" : "");

    public const string OtherWord = "Other";
    public const string MajorityWord = "Majority of Yes and No votes";
}
