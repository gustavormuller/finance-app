namespace Finance.Api.Application;

/// <summary>
/// The user the current operation belongs to, or <c>null</c> when there is none: an
/// unauthenticated request, or a background job.
/// </summary>
/// <remarks>
/// A port under ADR-015. The query filters and <c>Application/</c> need the caller's
/// identity, and neither should depend on <c>HttpContext</c> to get it.
/// </remarks>
public interface ICurrentUser
{
    Guid? Id { get; }
}
