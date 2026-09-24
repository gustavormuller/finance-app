namespace Finance.Api.Application;

/// <summary>
/// The user a background operation acts for, in a scope it opened for that user. Scoped:
/// set once, right after the scope is created, and never in a scope that serves a request.
/// <see cref="ICurrentUser"/> reads it before the request's claims, so the query filters
/// stay on for jobs too (ADR-007) and no production query needs <c>IgnoreQueryFilters</c>.
/// </summary>
public sealed class ActingUser
{
    public Guid? Id { get; private set; }

    /// <exception cref="InvalidOperationException">The scope already acts for a user.</exception>
    public void ActAs(Guid userId) =>
        Id = Id is null ? userId : throw new InvalidOperationException("This scope already acts for a user.");
}
