namespace CivicLens.Application.Paging;

public static class PageLimit
{
    public const int Default = 10;
    public const int Maximum = 100;

    public static void Validate(int limit)
    {
        if (limit is < 1 or > Maximum)
            throw new ArgumentOutOfRangeException(nameof(limit), "Page limit must be from 1 to 100.");
    }
}
