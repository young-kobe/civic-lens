using System.Security.Claims;
using CivicLens.Core.Review;

namespace CivicLens.Host.Review;

public sealed class ReviewActorAccessor(ReviewWorkspaceSettings settings)
{
    public ReviewActor GetActor(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true) throw new UnauthorizedAccessException();
        var subjects = principal.FindAll("sub").Select(claim => claim.Value).ToArray();
        if (subjects.Length != 1) throw new UnauthorizedAccessException();
        var subject = subjects[0];
        if (string.Equals(subject, settings.OwnerSubject, StringComparison.Ordinal))
            return new ReviewActor(subject, ReviewRole.Owner);
        if (settings.ReviewerSubjects.Contains(subject)) return new ReviewActor(subject, ReviewRole.Reviewer);
        throw new UnauthorizedAccessException();
    }

    public bool IsAuthorized(ClaimsPrincipal principal)
    {
        try { _ = GetActor(principal); return true; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
