namespace CivicLens.PublicWeb.Components.Ui;

public static class ToneClass
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
