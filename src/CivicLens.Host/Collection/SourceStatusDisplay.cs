using CivicLens.Application.Collection.Health;
using CivicLens.Host.Components.Ui;

namespace CivicLens.Host.Collection;

public sealed record SourceStatus(string Label, Tone Tone);

public static class SourceStatusDisplay
{
    public static SourceStatus Status(SourceHealth health) => health.State switch
    {
        SourceCheckState.Checking => new("Checking", Tone.Live),
        SourceCheckState.Failed when health.RetryAt is { } retryAt => new($"Failed, retrying {Moment.Short(retryAt)}", Tone.Warn),
        SourceCheckState.Failed => new("Failed", Tone.Bad),
        SourceCheckState.Cancelled => new("Stopped", Tone.Neutral),
        SourceCheckState.ChangeFound => new("Change found", Tone.Ok),
        SourceCheckState.BaselineSaved => new("First version saved", Tone.Neutral),
        SourceCheckState.UpToDate => new("Up to date", Tone.Ok),
        _ => new("Not checked yet", Tone.Neutral)
    };

    public static string Detail(SourceHealth health) => health.State switch
    {
        SourceCheckState.Checking when health.RetryAt is not null => "Text preparation will start again on its own.",
        SourceCheckState.Checking => "Collecting and preparing text. No action is needed.",
        SourceCheckState.Failed when health.RetryAt is not null => "This check will try again on its own.",
        SourceCheckState.Failed => "This check could not finish. This is a coverage gap, not evidence of no activity.",
        SourceCheckState.Cancelled => "This check was stopped.",
        SourceCheckState.ChangeFound => "The wording changed. Start a draft to review it.",
        SourceCheckState.BaselineSaved => "The first version is saved. Later checks can find changes.",
        SourceCheckState.UpToDate => "No new changes were found.",
        _ => "This source has not been checked yet."
    };

    public static HealthItem Item(string name, SourceHealth health)
    {
        var status = Status(health);
        return new(name, status.Label, status.Tone, health.LastCheckedAt);
    }
}
