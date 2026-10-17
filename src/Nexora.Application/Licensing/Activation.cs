using Nexora.Application.Abstractions;
using Nexora.Application.Security;

namespace Nexora.Application.Licensing;

public interface IActivationAccess
{
    bool IsActive { get; }
    void Demand();
}

/// <summary>Protects service access and database filters even when a screen has not yet refreshed.</summary>
public sealed class LicensedSessionContext(ISessionContext inner, IActivationAccess activation) : ISessionContext
{
    public SessionInfo? Current
    {
        get
        {
            if (activation.IsActive) return inner.Current;
            inner.Set(null);
            return null;
        }
    }
    public void Set(SessionInfo? session)
    {
        if (session is not null) activation.Demand();
        inner.Set(session);
    }
    public void RecordActivity() { if (activation.IsActive) inner.RecordActivity(); else inner.Set(null); }
    public bool TryUpdate(SessionInfo expected, SessionInfo updated) => activation.IsActive && inner.TryUpdate(expected, updated);
}
