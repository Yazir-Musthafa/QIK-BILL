using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Nexora.Application;
using Nexora.Application.Formatting;

namespace Nexora.Desktop.ViewModels;

public sealed record KpiItem(string Label, string Value, string Note);

/// <summary>A pair of bars; heights are fractions (0–1) of the tallest value, applied as a scale transform.</summary>
public sealed record TrendBar(string Label, double Revenue, double Expenses, string Tooltip);

public sealed record AgeingRow(string Label, string Amount, double Fraction);

public sealed record TopProductRow(string Name, string Amount, double Fraction);

public sealed record ActivityRow(string Time, string Title, string Detail);

public sealed record StockRow(string Sku, string Name, string Available, string ReorderLevel);

public sealed partial class DashboardViewModel(
    IWorkspaceService workspace,
    ILogger<DashboardViewModel> logger,
    Action<string> navigate) : ObservableObject
{
    public string Title => "Dashboard";

    public IReadOnlyList<QuickAction> QuickActions { get; } =
    [
        new("New invoice", "", "sales.invoices"),
        new("Purchase", "", "purchases.invoices"),
        new("Payment", "", "accounts.vouchers"),
        new("Receipt", "", "accounts.vouchers"),
        new("Product", "", "inventory.products"),
        new("Customer", "", "sales.customers"),
    ];

    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private string? _error;
    [ObservableProperty] private bool _isDemonstration;
    [ObservableProperty] private string _notice = string.Empty;
    [ObservableProperty] private string _subtitle = string.Empty;
    [ObservableProperty] private IReadOnlyList<KpiItem> _kpis = [];
    [ObservableProperty] private IReadOnlyList<TrendBar> _trend = [];
    [ObservableProperty] private IReadOnlyList<AgeingRow> _ageing = [];
    [ObservableProperty] private IReadOnlyList<TopProductRow> _topProducts = [];
    [ObservableProperty] private IReadOnlyList<ActivityRow> _activity = [];
    [ObservableProperty] private IReadOnlyList<StockRow> _lowStock = [];

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        Error = null;
        try
        {
            var session = workspace.CurrentSession;
            Subtitle = session is null ? string.Empty
                : $"{session.CompanyName} · FY {new Nexora.Domain.Companies.FinancialYear(session.FinancialYearStartYear).Label}";
            var snapshot = await workspace.GetDashboardAsync();
            Apply(snapshot);
        }
        catch (Exception exception)
        {
            logger.LogError("Dashboard failed to load; error type {ErrorType}", exception.GetType().FullName);
            Error = "The dashboard could not be loaded. Select Retry; if it keeps failing, restart QIK Suite.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void Open(string key) => navigate(key);

    private void Apply(DashboardSnapshot snapshot)
    {
        IsDemonstration = snapshot.IsDemonstration;
        Notice = snapshot.Notice;
        // Without posted data, show a dash rather than ₹0 so nothing reads as a computed balance.
        Kpis = snapshot.Metrics
            .Select(m => new KpiItem(m.Label, snapshot.IsDemonstration ? IndianNumberFormat.Amount(m.Value) : "—",
                snapshot.IsDemonstration ? m.Note : "No data yet"))
            .ToList();

        var trendMax = snapshot.Trend.Count == 0 ? 1m : Math.Max(1m, snapshot.Trend.Max(t => Math.Max(t.Revenue, t.Expenses)));
        Trend = snapshot.Trend.Select(t => new TrendBar(t.Label, (double)(t.Revenue / trendMax), (double)(t.Expenses / trendMax),
            $"{t.Label}: revenue {IndianNumberFormat.Amount(t.Revenue)}, expenses {IndianNumberFormat.Amount(t.Expenses)}")).ToList();

        var ageingMax = snapshot.ReceivablesAgeing.Count == 0 ? 1m : Math.Max(1m, snapshot.ReceivablesAgeing.Max(a => a.Value));
        Ageing = snapshot.ReceivablesAgeing
            .Select(a => new AgeingRow(a.Label, IndianNumberFormat.Amount(a.Value), (double)(a.Value / ageingMax))).ToList();

        var productMax = snapshot.TopProducts.Count == 0 ? 1m : Math.Max(1m, snapshot.TopProducts.Max(p => p.Revenue));
        TopProducts = snapshot.TopProducts
            .Select(p => new TopProductRow(p.Name, IndianNumberFormat.Amount(p.Revenue), (double)(p.Revenue / productMax))).ToList();

        Activity = snapshot.RecentActivity
            .Select(a => new ActivityRow(a.OccurredAt.ToLocalTime().ToString("HH:mm"), a.Title, a.Detail)).ToList();

        LowStock = snapshot.LowStock
            .Select(s => new StockRow(s.Sku, s.Name, IndianNumberFormat.Number(s.Available, 0), IndianNumberFormat.Number(s.ReorderLevel, 0)))
            .ToList();
    }
}
