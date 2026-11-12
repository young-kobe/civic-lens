namespace CivicLens.Application.Publication;

public enum BillCountStatus
{
    Verified,
    MissingRows,
    DifferentRows,
    /// <summary>The release expects no rows and holds none, so there is nothing to verify.</summary>
    NothingExpected
}
