namespace Nexora.Application;

public sealed record SetupRequest(string CompanyName, string DisplayName, string Username,
    string Password, string StateCode, int FinancialYearStartYear, bool CreateDemoCompany);
/// <summary>The signed-in user, the open company, and the user's role and permissions in that company.</summary>
public sealed record SessionInfo(Guid UserId, string Username, string DisplayName, Guid CompanyId,
    string CompanyName, bool IsDemo, int FinancialYearStartYear, Guid RoleId, string RoleName,
    IReadOnlySet<string> Permissions, decimal MaxDiscountPercent, bool MustChangePassword, DateTime? PasswordChangedUtc = null)
{
    public bool Has(string permission) => Permissions.Contains(permission);
}
public sealed record CompanySummary(Guid Id, string Name, bool IsDemo, int FinancialYearStartYear, string RoleName);
public sealed record LoginResult(bool Succeeded, string? Error, SessionInfo? Session);
public sealed record WorkspacePreferences(string Theme = "Light", bool SidebarCollapsed = false);
public sealed record DashboardMetric(string Label, decimal Value, string Note);
public sealed record TrendPoint(string Label, decimal Revenue, decimal Expenses);
public sealed record ActivityItem(DateTimeOffset OccurredAt, string Title, string Detail);
public sealed record StockAlert(string Sku, string Name, decimal Available, decimal ReorderLevel);
public sealed record ProductPerformance(string Name, decimal Revenue);
public sealed record AgeingBucket(string Label, decimal Value);
public sealed record DashboardSnapshot(bool IsDemonstration, string Notice,
    IReadOnlyList<DashboardMetric> Metrics, IReadOnlyList<TrendPoint> Trend,
    IReadOnlyList<ActivityItem> RecentActivity, IReadOnlyList<StockAlert> LowStock,
    IReadOnlyList<ProductPerformance> TopProducts, IReadOnlyList<AgeingBucket> ReceivablesAgeing, bool HasData = false);

/// <summary>Computes the dashboard of a real company from its posted data (implemented by the reporting module).</summary>
public interface IDashboardSource
{
    Task<DashboardSnapshot> GetAsync(CancellationToken cancellationToken = default);
}

/// <summary>Application boundary; storage providers remain outside this assembly.</summary>
public interface IWorkspaceService
{
    SessionInfo? CurrentSession { get; }
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<bool> NeedsSetupAsync(CancellationToken cancellationToken = default);
    Task<SessionInfo> CompleteSetupAsync(SetupRequest request, CancellationToken cancellationToken = default);
    Task<LoginResult> SignInAsync(string username, string password, CancellationToken cancellationToken = default);
    void SignOut();
    Task<IReadOnlyList<CompanySummary>> GetCompaniesAsync(CancellationToken cancellationToken = default);
    Task<SessionInfo> SwitchCompanyAsync(Guid companyId, CancellationToken cancellationToken = default);
    Task<DashboardSnapshot> GetDashboardAsync(CancellationToken cancellationToken = default);
    Task<WorkspacePreferences> GetPreferencesAsync(CancellationToken cancellationToken = default);
    Task SavePreferencesAsync(WorkspacePreferences preferences, CancellationToken cancellationToken = default);
    Task DeleteDemonstrationCompanyAsync(Guid companyId, CancellationToken cancellationToken = default);

    /// <summary>Changes the signed-in user's own password after checking the current one.</summary>
    Task ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default);

    /// <summary>Reloads the role and permissions of the current session, for example after a role was edited.</summary>
    Task RefreshSessionAsync(CancellationToken cancellationToken = default);
}

/// <summary>A user-facing rule violation. The message is safe to show and tells the user what to do.</summary>
public class WorkspaceValidationException(string message, Exception? innerException = null) : Exception(message, innerException);
