namespace CivicLens.Application.People;

/// <summary>The owner choices for one person. A person with no revision has <see cref="None"/>.</summary>
public sealed record PersonSetting(bool WithholdPhoto, string? Website)
{
    public static readonly PersonSetting None = new(false, null);
}
