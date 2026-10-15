namespace Nexora.Application.Abstractions;

/// <summary>
/// The signed-in user and company. Infrastructure reads it to scope every query and write to the
/// current company; Application reads it to check permissions.
/// </summary>
public interface ISessionContext
{
    SessionInfo? Current { get; }

    void Set(SessionInfo? session);

    /// <summary>Only real user input extends an active session. Expired sessions cannot be revived.</summary>
    void RecordActivity();

    /// <summary>Refresh identity/permissions without extending idle time or reviving an ended session.</summary>
    bool TryUpdate(SessionInfo expected, SessionInfo updated);
}

public sealed class SessionContext(TimeProvider? timeProvider = null) : ISessionContext
{
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(15);
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly object _gate = new();
    private SessionInfo? _current;
    private long _lastActivity;

    public SessionInfo? Current
    {
        get { lock (_gate) { Expire(); return _current; } }
    }

    public void Set(SessionInfo? session)
    {
        lock (_gate) { _current = session; _lastActivity = _time.GetTimestamp(); }
    }

    public void RecordActivity()
    {
        lock (_gate)
        {
            Expire();
            if (_current is not null) _lastActivity = _time.GetTimestamp();
        }
    }

    public bool TryUpdate(SessionInfo expected, SessionInfo updated)
    {
        lock (_gate)
        {
            Expire();
            if (!ReferenceEquals(_current, expected)) return false;
            _current = updated;
            return true;
        }
    }

    private void Expire()
    {
        if (_current is not null && _time.GetElapsedTime(_lastActivity) >= IdleTimeout) _current = null;
    }
}
