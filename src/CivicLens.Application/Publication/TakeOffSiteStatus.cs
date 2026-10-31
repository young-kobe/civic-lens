namespace CivicLens.Application.Publication;

public sealed record TakeOffSiteStatus(bool OnSite, int? WithdrawnRevisionNumber, bool ReadersSeeOldSite, bool RolledBack)
{
    public static TakeOffSiteStatus NotPublished { get; } = new(false, null, false, false);

    public bool TakenOff => WithdrawnRevisionNumber is not null;

    public bool CanTakeOff => OnSite || ReadersSeeOldSite;
}
