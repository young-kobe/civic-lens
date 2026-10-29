using System.Globalization;

namespace CivicLens.Publication.Contracts;

public sealed record PublishedExecutiveActionNotice
{
    public required string DocumentNumber { get; init; }
    public required DateOnly PublishedOn { get; init; }
    public required string Url { get; init; }

    public string Value => $"{DocumentNumber}, published {PublishedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}, {Url}";
}
