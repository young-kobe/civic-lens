using CivicLens.Publication.Contracts;
using CivicLens.PublicWeb.Components.Ui;

namespace CivicLens.PublicWeb.Publication;

/// <summary>
/// The one owner of the latest official action line of a bill row: the bill list, the issue page, and the bill rows of the official and record pages.
/// The text and the date come from the stored status of the active release. A bill with no stored status has no line. The precedent is <see cref="BillIntroducedWords"/>.
/// </summary>
public static class BillLatestActionWords
{
    /// <summary>The line, such as "Latest official action 13 Feb 2026: In committee: sent to the House Committee on Energy and Commerce.", or null with no stored status.</summary>
    public static string? Line(string? text, DateOnly? day, string billType) =>
        text is not null && day is { } actionOn ? $"Latest official action {Moment.Day(actionOn)}: {BillStatusMeaning.Plain(text, billType)}" : null;
}
