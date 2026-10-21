namespace CivicLens.Host.Publication;

public sealed record TimelineEvent(DateOnly Day, string Text, TimeOnly Time = default);
