using CivicLens.Host.Components.Ui;
using CivicLens.Publication.Contracts;

namespace CivicLens.Host.Publication;

internal static class PublishedTimelineView
{
    public static IReadOnlyList<TimelineEvent> For(PublishedDocumentChange record)
    {
        List<TimelineEvent> events = [];
        if (record.ChangeDate is { } changed) events.Add(new(changed, "The source changed"));
        events.Add(At(record.Before.ObservedAtUtc, "Earlier version observed"));
        events.Add(At(record.After.ObservedAtUtc, "Later version observed"));
        return WithPublication(events, record.ApprovedAtUtc, record.FirstPublishedAtUtc, record.Corrections);
    }

    public static IReadOnlyList<TimelineEvent> For(PublishedStatement record)
    {
        List<TimelineEvent> events = [];
        if (record.StatementDate is { } said) events.Add(new(said, "Statement made"));
        events.Add(At(record.Source.ObservedAtUtc, "Source observed"));
        return WithPublication(events, record.ApprovedAtUtc, record.FirstPublishedAtUtc, record.Corrections);
    }

    public static IReadOnlyList<TimelineEvent> For(PublishedAction record)
    {
        List<TimelineEvent> events = [];
        foreach (var sponsorship in record.Sponsorships)
        {
            var who = $"{sponsorship.Official.Name} ({PublishedRecordLabels.Role(sponsorship.Role).ToLowerInvariant()})";
            events.Add(new(sponsorship.SponsoredOn, $"Sponsored by {who}"));
            if (sponsorship.WithdrawnOn is { } withdrawn) events.Add(new(withdrawn, $"Withdrawn by {who}"));
        }
        return WithPublication(events, record.ApprovedAtUtc, record.FirstPublishedAtUtc, record.Corrections);
    }

    public static IReadOnlyList<TimelineEvent> For(PublishedVote record) =>
        WithPublication([new(record.VotedOn, "Vote held")], record.ApprovedAtUtc, record.FirstPublishedAtUtc, record.Corrections);

    public static IReadOnlyList<TimelineEvent> For(PublishedExecutiveAction record) =>
        WithPublication([new(record.SignedOn, "Signed by the President"), new(record.PublishedOn, "Published in the Federal Register"),
            .. record.CorrectionNotices.Select(notice => new TimelineEvent(notice.PublishedOn, PublishedExecutiveActionView.CorrectionText(notice)))],
            record.ApprovedAtUtc, record.FirstPublishedAtUtc, record.Corrections);

    public static IReadOnlyList<TimelineEvent> For(PublishedRelationship record)
    {
        List<TimelineEvent> events = [];
        if (record.StatementDate is { } said) events.Add(new(said, "Statement made"));
        return WithPublication(events, record.ApprovedAtUtc, record.FirstPublishedAtUtc, record.Corrections);
    }

    public static IReadOnlyList<TimelineEvent> For(PublishedBrief record) =>
        WithPublication([new(record.WeekStart, "Week starts")], record.ApprovedAtUtc, record.FirstPublishedAtUtc, record.Corrections);

    private static TimelineEvent At(DateTimeOffset at, string text) =>
        new(DateOnly.FromDateTime(at.UtcDateTime), $"{text}, {Moment.Full(at)}", TimeOnly.FromDateTime(at.UtcDateTime));

    private static IReadOnlyList<TimelineEvent> WithPublication(List<TimelineEvent> events, DateTimeOffset approvedAtUtc,
        DateTimeOffset firstPublishedAtUtc, PublishedCorrection[] corrections)
    {
        events.Add(At(approvedAtUtc, "Approved"));
        events.Add(At(firstPublishedAtUtc, "First published"));
        events.AddRange(corrections.Select(correction => At(correction.PublishedAtUtc, PublishedCorrectionView.TimelineText(correction))));
        return [.. events.OrderBy(item => item.Day).ThenBy(item => item.Time)];
    }
}
