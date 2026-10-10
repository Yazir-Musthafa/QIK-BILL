namespace Nexora.Application.Abstractions;

/// <summary>
/// The signed-in user and company. Infrastructure reads it to scope every query and write to the
/// current company; Application reads it to check permissions.
/// </summary>
public interface ISessionContext
{
    SessionInfo? Current { get; }

    void Set(SessionInfo? session);
}

public sealed class SessionContext : ISessionContext
{
    private volatile SessionInfo? _current;

    public SessionInfo? Current => _current;

    public void Set(SessionInfo? session) => _current = session;
}
