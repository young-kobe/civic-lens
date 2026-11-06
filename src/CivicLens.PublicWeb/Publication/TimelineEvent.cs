namespace CivicLens.PublicWeb.Publication;

public sealed record TimelineEvent(DateOnly Day, string Text, TimeOnly Time = default);
