namespace CivicLens.Host.Publication;

internal sealed class GlossaryPageUse
{
    private readonly HashSet<string> shown = [];

    public bool IsFirstUse(string key) => shown.Add(key);
}
