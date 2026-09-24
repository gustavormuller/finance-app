using System.Security.Claims;
using Finance.Api.Application;

namespace Finance.Api.Infrastructure;

/// <summary>
/// Reads the current user from the authentication cookie's <c>NameIdentifier</c>
/// claim, unless the scope was opened by a job for one user (<see cref="ActingUser"/>).
/// The only production implementation of <see cref="ICurrentUser"/>.
/// </summary>
/// <remarks>
/// Returns <c>null</c> rather than throwing when there is no authenticated caller:
/// an unauthenticated request is a normal state, and the query filters have to
/// resolve to "no rows" in that case rather than blow up.
/// </remarks>
public sealed class HttpCurrentUser(IHttpContextAccessor httpContextAccessor, ActingUser actingUser) : ICurrentUser
{
    public Guid? Id => actingUser.Id ?? (
        Guid.TryParse(
            httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier),
            out var id)
            ? id
            : null);
}
