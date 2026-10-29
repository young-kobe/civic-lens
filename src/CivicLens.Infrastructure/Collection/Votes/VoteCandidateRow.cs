using CivicLens.Core.Votes;

namespace CivicLens.Infrastructure.Collection.Votes;

internal sealed class VoteCandidateRow
{
    public string CandidateId { get; set; } = "";
    public string SourceId { get; set; } = "";
    public Chamber Chamber { get; set; }
    public int Congress { get; set; }
    public int SessionNumber { get; set; }
    public int RollCallNumber { get; set; }
    public long UpdateDateUtcTicks { get; set; }
    public string? BillType { get; set; }
    public int? BillNumber { get; set; }
    public string DetailJobId { get; set; } = "";
    public string MembersJobId { get; set; } = "";
    public string? RosterJobId { get; set; }
    public string DetailExtractionId { get; set; } = "";
    public string MembersExtractionId { get; set; } = "";
    public string ConfigurationRevisionId { get; set; } = "";
    public long CreatedAtUtcTicks { get; set; }
}
