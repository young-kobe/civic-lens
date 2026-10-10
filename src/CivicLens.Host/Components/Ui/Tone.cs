namespace CivicLens.Host.Components.Ui;

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
