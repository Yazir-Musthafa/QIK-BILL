namespace Nexora.Application;

public sealed record SetupRequest(string CompanyName, string DisplayName, string Username,
    string Password, string StateCode, int FinancialYearStartYear, bool CreateDemoCompany);
public sealed record SessionInfo(Guid UserId, string Username, string DisplayName, Guid CompanyId,
    string CompanyName, bool IsDemo, int FinancialYearStartYear);
public sealed record CompanySummary(Guid Id, string Name, bool IsDemo, int FinancialYearStartYear);
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
    IReadOnlyList<ProductPerformance> TopProducts, IReadOnlyList<AgeingBucket> ReceivablesAgeing);

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
}

public sealed class WorkspaceValidationException(string message) : Exception(message);
