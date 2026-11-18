using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>The approved Maps wording and the selected layer's exact display values.</summary>
public static class MapsWords
{
    public const string Heading = "Maps";
    public const string Intro = "Explore published records for one Congress.";
    public const string Explore = "Explore maps";
    public const string Unknown = "Data unavailable.";
    public const string UnknownSeat = "Seat position unavailable.";
    public const string EmptyBase = "No published records for this measure.";
    public const string Empty = "This release has no matching seat terms.";
    public const string NoGeometry = "The district map is unavailable for this Congress. Use the tiles or table.";
    public const string Hint = "Hover, focus or tap a seat for details. Open the member link to see their page.";
    public const string TableNote = "Use the table for the same figures and member links.";
    public const string Limits = "These maps show published records. They make no predictions and give no ratings.";
    public const string Scale = "Fill shows the share.";
    public const string TermNote = "Term ends use exact dates saved with the release.";
    public const string OtherDates = "Other saved end dates";

    public static readonly SearchChoiceData[] Layers = [new("attendance", "Attendance"), new("termEnds", "Term ends"), new("issueActivity", "Issue activity")];
    public static readonly SearchChoiceData[] Measures = [new("bills", "Sponsored bills"), new("votes", "Substantive votes cast")];
    public static readonly SearchChoiceData[] Chambers = [new("house", "House"), new("senate", "Senate")];

    public static string Caption(MapsPageData data) => data.Congress is { } congress
        ? $"Published {data.Chamber} figures for the {CongressCalendar.Ordinal(congress)} Congress." : Intro;

    public static string Scope(MapsPageData data) => data.Congress is not { } congress ? Unknown : data.Layer switch
    {
        "attendance" => $"Published {data.Chamber} roll calls in the {CongressCalendar.Ordinal(congress)} Congress that list each member.",
        "issueActivity" => $"Each member's published activity in the {CongressCalendar.Ordinal(congress)} Congress, across both chambers.",
        _ => TermNote
    };

    public static MapFraction? Fraction(MapsPageData data, MapMemberData member) => data.Layer == "attendance" ? member.Attendance
        : data.Measure == "votes" ? member.Votes : member.Bills;

    public static string Figure(MapFraction? fraction) => fraction is null ? Unknown
        : fraction.Base == 0 ? $"{fraction.Text}. {EmptyBase}" : fraction.Text;

    public static string TermEnd(DateOnly? date) => date is { } end ? Components.Ui.Moment.Day(end) : Unknown;

    public static string Detail(MapsPageData data, MapMemberData member) =>
        $"{member.Seat}: {member.Name}. {member.Party ?? Unknown}. " + (data.Layer == "termEnds"
            ? $"Term ends: {TermEnd(member.TermEndsOn)}" : Figure(Fraction(data, member)));
}
