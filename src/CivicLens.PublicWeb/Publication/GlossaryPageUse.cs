namespace CivicLens.PublicWeb.Publication;

public sealed class GlossaryPageUse
{
    private readonly HashSet<string> shown = [];

    public bool IsFirstUse(string key) => shown.Add(key);
}
