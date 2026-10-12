using Microsoft.Extensions.DependencyInjection;
using Nexora.Domain.Billing;
using Nexora.Domain.Accounting;
using Nexora.Reporting.Reports;
using Nexora.Reporting.Reports.Generators;

namespace Nexora.Reporting;

public static class DependencyInjection
{
    /// <summary>Registers the reports, exports, imports and dashboard figures.</summary>
    public static IServiceCollection AddNexoraReporting(this IServiceCollection services)
    {
        services.AddSingleton<IReportGeneratorMarker>(new PeriodSummaryGenerator("sales.summary", sales: true));
        services.AddSingleton<IReportGeneratorMarker>(new RegisterGenerator("sales.register", sales: true));
        services.AddSingleton<IReportGeneratorMarker, ProductSalesGenerator>();
        services.AddSingleton<IReportGeneratorMarker>(new PartySalesGenerator("sales.customers", sales: true));
        services.AddSingleton<IReportGeneratorMarker, SalespersonGenerator>();
        services.AddSingleton<IReportGeneratorMarker>(new PeriodSummaryGenerator("purchases.summary", sales: false));
        services.AddSingleton<IReportGeneratorMarker>(new RegisterGenerator("purchases.register", sales: false));
        services.AddSingleton<IReportGeneratorMarker>(new PartySalesGenerator("purchases.suppliers", sales: false));
        services.AddSingleton<IReportGeneratorMarker, ProfitPeriodsGenerator>();
        services.AddSingleton<IReportGeneratorMarker, ValuationGenerator>();
        services.AddSingleton<IReportGeneratorMarker, MovementGenerator>();
        services.AddSingleton<IReportGeneratorMarker, LowStockGenerator>();
        services.AddSingleton<IReportGeneratorMarker, BatchGenerator>();
        services.AddSingleton<IReportGeneratorMarker>(new BalancesGenerator("parties.receivables", PartyKind.Customer));
        services.AddSingleton<IReportGeneratorMarker>(new BalancesGenerator("parties.payables", PartyKind.Supplier));
        services.AddSingleton<IReportGeneratorMarker>(new AgeingGenerator("ageing.debtors", PartyKind.Customer));
        services.AddSingleton<IReportGeneratorMarker>(new AgeingGenerator("ageing.creditors", PartyKind.Supplier));
        services.AddSingleton<IReportGeneratorMarker>(new CashBookGenerator("cash.book", AccountKind.Cash));
        services.AddSingleton<IReportGeneratorMarker>(new CashBookGenerator("bank.book", AccountKind.Bank));
        services.AddSingleton<IReportGeneratorMarker, BankReconciliationGenerator>();
        services.AddSingleton<IReportGeneratorMarker, ExpenseSummaryGenerator>();
        services.AddSingleton<IReportGeneratorMarker, GstSummaryGenerator>();
        services.AddSingleton<IReportGeneratorMarker, HsnSummaryGenerator>();
        services.AddSingleton<IReportGeneratorMarker, LedgerGenerator>();
        services.AddSingleton<IReportGeneratorMarker, TrialBalanceGenerator>();
        services.AddSingleton<IReportGeneratorMarker, ProfitAndLossGenerator>();
        services.AddSingleton<IReportGeneratorMarker, BalanceSheetGenerator>();
        services.AddSingleton<IReportGeneratorMarker, CashFlowGenerator>();
        services.AddSingleton<IReportGeneratorMarker, CostCentreGenerator>();
        services.AddSingleton<IReportGeneratorMarker, ServicePerformanceGenerator>();
        services.AddSingleton<ReportService>();
        services.AddSingleton<Import.DataTransferService>();
        services.AddSingleton<Application.IDashboardSource, Dashboard.DashboardSource>();
        services.AddSingleton<Export.ReportExportService>();
        return services;
    }
}
