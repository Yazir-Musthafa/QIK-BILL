using Nexora.Application.Abstractions;

namespace Nexora.Application.Security;

/// <summary>
/// Permission checks for Application services. Hiding a button is not enough: every protected
/// service method calls <see cref="Demand"/> before it reads or changes data.
/// </summary>
public interface IAuthorizer
{
    /// <summary>Returns the session, or throws if nobody is signed in or a password change is pending.</summary>
    SessionInfo RequireSession();

    /// <summary>Returns the session if it holds <paramref name="permission"/>; otherwise throws <see cref="AccessDeniedException"/>.</summary>
    SessionInfo Demand(string permission);

    bool Has(string permission);
}

public sealed class Authorizer(ISessionContext session) : IAuthorizer
{
    public SessionInfo RequireSession()
    {
        var current = session.Current ?? throw new AccessDeniedException("Your session has ended. Sign in again.");
        if (current.MustChangePassword) throw new AccessDeniedException("Change your password before continuing.");
        return current;
    }

    public SessionInfo Demand(string permission)
    {
        var current = RequireSession();
        if (!current.Has(permission))
        {
            throw new AccessDeniedException(
                $"Your role ({current.RoleName}) does not allow this. Ask an administrator for the \"{Describe(permission)}\" permission.");
        }

        return current;
    }

    public bool Has(string permission) => session.Current is { MustChangePassword: false } current && current.Has(permission);

    private static string Describe(string permission)
    {
        var info = Domain.Security.Permissions.All.FirstOrDefault(p => p.Key == permission);
        return info is null ? permission : $"{info.Module}: {info.Action}";
    }
}

public sealed class AccessDeniedException(string message) : WorkspaceValidationException(message);
