namespace CivicLens.Application.Publication;

public sealed record TakeOffSiteStatus(bool OnSite, bool TakenOff, bool ReadersSeeOldSite, bool RolledBack)
{
    public static TakeOffSiteStatus NotPublished { get; } = new(false, false, false, false);

    public bool CanTakeOff => OnSite || ReadersSeeOldSite;
}
