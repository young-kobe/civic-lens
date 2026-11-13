using System.Globalization;
using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>
/// The one owner of the tenure rule of an office holder: unbroken stretches of terms and the sentence "In the House since January 2019."
/// The static official page and the server official page both call it. Only a House or Senate term has a chamber, so any other office has no tenure sentence.
/// </summary>
public static class OfficeTenure
{
    private const int MaxSwearingInGapDays = 7;

    /// <summary>The tenure sentence of the newest unbroken stretch in one office, or null when no started term is a House or Senate term.</summary>
    public static string? Of(IEnumerable<PublishedOfficeTerm> terms, DateOnly publishedOn)
    {
        if (Stretches(terms, publishedOn, (earlier, later) => earlier.Office == later.Office).LastOrDefault() is not { } stretch || ChamberOf(stretch.Latest.Kind) is not { } chamber)
            return null;
        var since = MonthYear(stretch.Start);
        return OfficeLede.HasEnded(stretch.End, publishedOn)
            ? $"In {chamber} from {since} to {MonthYear(stretch.End!.Value)}."
            : $"In {chamber} since {since}.";
    }

    /// <summary>The stretches of the terms that started on or before the release day. Two terms join one stretch when <paramref name="sameSeat"/> holds and the next one starts within a swearing-in gap.</summary>
    public static IReadOnlyList<PublishedOfficeStretch> Stretches(IEnumerable<PublishedOfficeTerm> terms, DateOnly publishedOn, Func<PublishedOfficeTerm, PublishedOfficeTerm, bool> sameSeat)
    {
        var stretches = new List<PublishedOfficeStretch>();
        foreach (var term in terms.Where(term => term.Start <= publishedOn).OrderBy(term => term.Start))
        {
            if (stretches.Count > 0 && stretches[^1] is { } last && sameSeat(last.Latest, term) && Continues(last.End, term.Start))
                stretches[^1] = last with { Latest = term, End = term.End };
            else
                stretches.Add(new(term, term.Start, term.End));
        }
        return stretches;
    }

    private static bool Continues(DateOnly? end, DateOnly nextStart) => end is { } day && nextStart <= day.AddDays(MaxSwearingInGapDays);

    private static string? ChamberOf(PublishedOfficeKind kind) => kind switch
    {
        PublishedOfficeKind.Senator => "the Senate",
        PublishedOfficeKind.Representative => "the House",
        _ => null
    };

    private static string MonthYear(DateOnly day) => day.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
}
