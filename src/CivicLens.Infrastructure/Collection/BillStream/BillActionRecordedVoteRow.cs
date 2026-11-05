using CivicLens.Core.Votes;

namespace CivicLens.Infrastructure.Collection.BillStream;

internal sealed class BillActionRecordedVoteRow
{
    public string DetailJobId { get; set; } = "";
    public int ActionPosition { get; set; }
    public int Position { get; set; }
    public Chamber Chamber { get; set; }
    public int Congress { get; set; }
    public int SessionNumber { get; set; }
    public int RollNumber { get; set; }
    public long? VotedAtUtcTicks { get; set; }
    public string? Url { get; set; }
}
