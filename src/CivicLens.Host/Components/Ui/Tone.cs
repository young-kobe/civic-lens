namespace CivicLens.Host.Components.Ui;

/// <summary>Meaning carried by color: status badges, stats, notices, health dots, and feed markers.</summary>
public enum Tone
{
    Neutral,
    Ok,
    Warn,
    Bad,
    Live
}

internal static class ToneClass
{
    public static string Suffix(Tone tone) => tone switch
    {
        Tone.Ok => "ok",
        Tone.Warn => "warn",
        Tone.Bad => "bad",
        Tone.Live => "live",
        _ => "neutral"
    };
}
