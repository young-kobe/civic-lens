namespace CivicLens.Application.Issues;

public static class IssueIcons
{
    public const string Default = "document";

    public static readonly IReadOnlyDictionary<string, string> Paths = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [Default] = "M6 3h9l4 4v14H6zM14 3v5h5M9 13h7M9 17h7",
        ["border-map"] = "M3 6l6-3 6 3 6-3v15l-6 3-6-3-6 3zM9 3v15M15 6v15",
        ["trade-arrows"] = "M3 8h15l-3-3M21 16H6l3 3",
        ["dollar"] = "M12 3v18M17 7H9.5a3 3 0 0 0 0 6h5a3 3 0 0 1 0 6H6",
        ["receipt"] = "M6 3h12v18l-3-2-3 2-3-2-3 2zM9 8h6M9 12h6",
        ["medical-cross"] = "M9 3h6v6h6v6h-6v6H9v-6H3V9h6z",
        ["heart"] = "M12 21s-7-4.6-9.2-9A5 5 0 0 1 12 7a5 5 0 0 1 9.2 5c-2.2 4.4-9.2 9-9.2 9z",
        ["crosshair"] = "M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18zM12 8a4 4 0 1 0 0 8 4 4 0 0 0 0-8zM12 1v4M12 19v4M1 12h4M19 12h4",
        ["bolt"] = "M13 2 4 14h7l-1 8 9-12h-7z",
        ["government-building"] = "M4 21h16M6 21V8l6-4 6 4v13M10 21v-5h4v5",
        ["ballot"] = "M4 4h16v16H4zM8 12l3 3 5-6",
        ["globe"] = "M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18zM3 12h18M12 3c3 3 3 15 0 18M12 3c-3 3-3 15 0 18",
        ["flag"] = "M5 21V4h13l-3 4 3 4H5",
        ["compass"] = "M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18zM15.5 8.5l-2 5-5 2 2-5z",
        ["chip"] = "M7 7h10v10H7zM10 3v4M14 3v4M10 17v4M14 17v4M3 10h4M3 14h4M17 10h4M17 14h4",
        ["people"] = "M8 11a3 3 0 1 0 0-6 3 3 0 0 0 0 6zM2 20c0-3.5 2.7-6 6-6s6 2.5 6 6M16 5a3 3 0 0 1 0 6M22 20c0-3-2-5.2-4.5-5.8",
        ["fork-and-knife"] = "M6 3v8a3 3 0 0 0 6 0V3M9 3v18M17 3c-2 2-3 5-3 8h3v10"
    };

    public static string PathOf(string? name) => Paths[name ?? Default];

    public static bool IsKnown(string name) => Paths.ContainsKey(name);
}
