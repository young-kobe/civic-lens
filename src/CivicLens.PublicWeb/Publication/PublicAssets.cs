namespace CivicLens.PublicWeb.Publication;

public static class PublicAssets
{
    public static readonly IReadOnlyList<string> Stylesheets = ["assets/css/theme.css", "assets/css/app.css", "assets/css/public.css"];

    public const string ThemeScript = "assets/js/theme.js";

    public const string Script = "assets/js/app.js";

    public const string PublicScript = "assets/js/public.js";

    public const string Favicon = "assets/favicon.svg";

    public const string SchibstedGroteskFont = "assets/fonts/schibsted-grotesk.woff2";

    public const string NewsreaderFont = "assets/fonts/newsreader.woff2";

    public static readonly IReadOnlyList<string> FontLicenses = ["assets/fonts/ofl-schibsted-grotesk.txt", "assets/fonts/ofl-newsreader.txt"];

    public static IReadOnlyList<string> Paths { get; } =
        [.. Stylesheets.Append(ThemeScript).Append(Script).Append(PublicScript).Append(Favicon).Append(SchibstedGroteskFont).Append(NewsreaderFont).Concat(FontLicenses)];
}
