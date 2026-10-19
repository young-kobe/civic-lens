using System.Collections.Immutable;
using System.Globalization;
using CivicLens.Application.Issues;
using CivicLens.Application.Review;
using CivicLens.Core.Review;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace CivicLens.Host.Review;

public abstract class DraftListPage : ComponentBase
{
    private const string DateFormat = "yyyy-MM-dd";

    protected ReviewActor actor = default!;
    protected string? error;

    [Inject] protected ReviewActorAccessor Actors { get; set; } = default!;
    [Inject] protected IssueConfiguration Issues { get; set; } = default!;
    [Inject] private ReviewOfficials Officials { get; set; } = default!;

    [CascadingParameter] protected HttpContext HttpContext { get; set; } = default!;
    [CascadingParameter] protected Task<AuthenticationState> AuthenticationState { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "drafts")] protected string? DraftsFilterName { get; set; }
    [SupplyParameterFromQuery(Name = "draftsCursor")] protected string? DraftsCursor { get; set; }
    [SupplyParameterFromQuery(Name = "issue")] protected string? IssueId { get; set; }
    [SupplyParameterFromQuery(Name = "official")] protected string? OfficialId { get; set; }
    [SupplyParameterFromQuery(Name = "from")] protected string? From { get; set; }
    [SupplyParameterFromQuery(Name = "to")] protected string? To { get; set; }

    protected ImmutableArray<KeyValuePair<string, string>> OfficialChoices => Officials.People;

    protected CancellationToken RequestAborted => HttpContext.RequestAborted;
    protected DraftStatusFilter Filter => DraftFilterNames.Parse(DraftsFilterName);

    protected async Task ReadActorAsync() => actor = Actors.GetActor((await AuthenticationState).User);

    protected async Task LoadGuardedAsync(Func<Task> load, string unavailableMessage)
    {
        try
        {
            await load();
        }
        catch (OperationCanceledException) when (RequestAborted.IsCancellationRequested) { throw; }
        catch (DateRangeException exception)
        {
            error = exception.Message;
        }
        catch (ArgumentException)
        {
            error = "This page link is no longer valid. Go back to the first page of the list.";
        }
        catch (Exception)
        {
            error = unavailableMessage;
        }
    }

    protected DraftListFilter ReadListFilter()
    {
        var (from, to) = (ReadDate(From), ReadDate(To));
        if (from > to) throw new DateRangeException();
        return new(Filter, Choose(IssueId, Issues.Issues.Select(issue => issue.Id)),
            Choose(OfficialId, Officials.People.Select(person => person.Key)), from, to);
    }

    protected string Href(string route, string? statusName, string? draftsCursor, string fragment, params string?[] otherParts)
    {
        var parts = new[]
        {
            Part("drafts", statusName), Part("draftsCursor", draftsCursor), Part("issue", IssueId), Part("official", OfficialId),
            Part("from", From), Part("to", To)
        }.Concat(otherParts).OfType<string>().ToArray();
        return route + (parts.Length == 0 ? "" : "?" + string.Join('&', parts)) + "#" + fragment;
    }

    protected string ClearHref(string route) =>
        route + (Part("drafts", DraftFilterNames.NameOf(Filter)) is { } status ? "?" + status : "") + "#drafts";

    protected static string? Part(string name, string? value) =>
        string.IsNullOrEmpty(value) ? null : name + "=" + Uri.EscapeDataString(value);

    private static string? Choose(string? value, IEnumerable<string> configured)
    {
        if (string.IsNullOrEmpty(value)) return null;
        return configured.Contains(value, StringComparer.Ordinal) ? value : throw new ArgumentException("A list filter is not valid.");
    }

    private static DateOnly? ReadDate(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        return DateOnly.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date : throw new ArgumentException("A list filter is not valid.");
    }

    private sealed class DateRangeException() : ArgumentException("The start date must not be after the end date.");
}
