using Nexora.Application.Abstractions;
using Nexora.Application.Security;

namespace Nexora.Application.Licensing;

public sealed record InstallationStatus(string Status);
public sealed record ActivationHistory(DateTimeOffset AtUtc, string Actor, string Action, string Note);
public sealed record RegisteredCustomer(string Username, string DisplayName);
public interface ILocalActivationStore
{
    bool IsActive { get; }
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<InstallationStatus> GetAsync(CancellationToken cancellationToken = default);
    Task<bool> VerifyAdministratorAsync(string password, CancellationToken cancellationToken = default);
    Task ChangeAsync(string status, string note, string actor, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ActivationHistory>> HistoryAsync(CancellationToken cancellationToken = default);
}

// This session is deliberately separate from company/staff permissions.
public sealed class LocalAdministrator(ILocalActivationStore store, TimeProvider? time = null) : IAuthorizer
{
    public const string Username = "admin";
    public const string ManagePermission = "licensing.manage";
    private readonly SessionContext _session = new(time);
    public bool IsSignedIn => _session.Current is not null;
    public static bool IsAdminUsername(string? username) => string.Equals(username?.Trim(), Username, StringComparison.OrdinalIgnoreCase);
    public async Task<bool> SignInAsync(string username, string password)
    {
        SignOut();
        if (!IsAdminUsername(username) || !await store.VerifyAdministratorAsync(password)) return false;
        _session.Set(new SessionInfo(Guid.Empty, Username, "Activation administrator", Guid.Empty, "This installation", false, 2026,
            Guid.Empty, "Activation administrator", new HashSet<string> { ManagePermission }, 0, false));
        return true;
    }
    public void SignOut() => _session.Set(null);
    public void RecordActivity() => _session.RecordActivity();
    public SessionInfo RequireSession() => _session.Current ?? throw new AccessDeniedException("Sign in as the activation administrator.");
    public SessionInfo Demand(string permission)
    {
        var session = RequireSession();
        if (!session.Has(permission)) throw new AccessDeniedException("This permission is not available.");
        return session;
    }
    public bool Has(string permission) => _session.Current?.Has(permission) == true;
}

public sealed class LocalAdministrationService(ILocalActivationStore store, IWorkspaceStore workspace, IAuthorizer authorizer)
{
    public async Task<IReadOnlyList<RegisteredCustomer>> CustomersAsync(CancellationToken cancellationToken = default)
    {
        authorizer.Demand(LocalAdministrator.ManagePermission);
        var result = new List<RegisteredCustomer>();
        foreach (var id in await workspace.ListAllUserIdsAsync(cancellationToken))
            if (await workspace.FindUserAsync(id, cancellationToken) is { } user) result.Add(new(user.Username, user.DisplayName));
        return result.OrderBy(x => x.Username).ToList();
    }
    public Task<InstallationStatus> StatusAsync(CancellationToken cancellationToken = default)
    {
        authorizer.Demand(LocalAdministrator.ManagePermission);
        return store.GetAsync(cancellationToken);
    }
    public Task<IReadOnlyList<ActivationHistory>> HistoryAsync(CancellationToken cancellationToken = default)
    {
        authorizer.Demand(LocalAdministrator.ManagePermission);
        return store.HistoryAsync(cancellationToken);
    }
    public async Task ChangeAsync(string status, string note, CancellationToken cancellationToken = default)
    {
        var admin = authorizer.Demand(LocalAdministrator.ManagePermission);
        if (status is not ("Active" or "Suspended")) throw new WorkspaceValidationException("Choose Activate or Suspend.");
        if (string.IsNullOrWhiteSpace(note) || note.Trim().Length > 500) throw new WorkspaceValidationException("Enter a reason of 1–500 characters.");
        if (!await workspace.HasAnyUserAsync(cancellationToken)) throw new WorkspaceValidationException("Register the customer before activating this installation.");
        authorizer.Demand(LocalAdministrator.ManagePermission);
        await store.ChangeAsync(status, note.Trim(), admin.Username, cancellationToken);
    }
}
