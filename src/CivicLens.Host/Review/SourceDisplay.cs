using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Health;
using CivicLens.Host.Components.Ui;

namespace CivicLens.Host.Review;

internal static class SourceDisplay
{
    public static string Name(CollectionConfiguration? configuration, string sourceId)
    {
        var source = configuration?.Sources.FirstOrDefault(item => item.Id == sourceId);
        if (source is null) return sourceId;
        var names = configuration!.People
            .Where(person => source.Coverage?.Any(item => item.PersonId == person.Id) == true ||
                source.PersonIds?.Contains(person.Id) == true)
            .Select(person => person.Name);
        var label = string.Join(", ", names);
        return label.Length == 0 ? Document(source.Url) : label;
    }

    public static string Document(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)) return url;
        var last = parsed.AbsolutePath.Trim('/').Split('/').LastOrDefault();
        return string.IsNullOrEmpty(last) ? parsed.Host : Uri.UnescapeDataString(last).Replace('-', ' ');
    }

    public static HealthItem Health(CollectionConfiguration? configuration, SourceHealth health) =>
        new(Name(configuration, health.SourceId), Detail(health), Tone(health), health.LastCheckedAt);

    private static string Detail(SourceHealth health) => health.State switch
    {
        SourceCheckState.NeverChecked => "Not checked yet",
        SourceCheckState.Checking => health.RetryAt is null ? "Checking now" : "Preparing evidence",
        SourceCheckState.Failed => health.NeedsAttention ? "Check failed" : "Check failed, retrying by itself",
        SourceCheckState.Cancelled => "Check stopped",
        SourceCheckState.ChangeFound => "Change found",
        SourceCheckState.BaselineSaved => "Baseline saved",
        _ => "No change"
    };

    private static Tone Tone(SourceHealth health) => health.State switch
    {
        _ when health.NeedsAttention => Components.Ui.Tone.Bad,
        SourceCheckState.Failed => Components.Ui.Tone.Warn,
        SourceCheckState.Checking => Components.Ui.Tone.Live,
        SourceCheckState.NeverChecked or SourceCheckState.Cancelled => Components.Ui.Tone.Neutral,
        _ => Components.Ui.Tone.Ok
    };
}
