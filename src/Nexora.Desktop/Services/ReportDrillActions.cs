using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Nexora.Application;
using Nexora.Application.Billing;
using Nexora.Application.Expenses;
using Nexora.Application.Service;
using Nexora.Domain.Billing;
using Nexora.Desktop.Services.Printing;
using Nexora.Desktop.ViewModels.Accounting;
using Nexora.Desktop.ViewModels.Billing;
using Nexora.Desktop.ViewModels.Inventory;
using Nexora.Desktop.ViewModels.Expenses;
using Nexora.Desktop.ViewModels.Service;
using Nexora.Desktop.ViewModels.Settings;
using Nexora.Printing;
using Nexora.Reporting.Reports;

namespace Nexora.Desktop.Services;

/// <summary>Opens the source through its existing service and screen, preserving their permission checks.</summary>
public sealed class ReportDrillActions(IServiceProvider services)
{
    public async Task OpenAsync(DrillTarget target)
    {
        object page;
        switch (target.Kind)
        {
            case DrillKind.BillingDocument:
                var document = await services.GetRequiredService<BillingService>().GetAsync(target.Id);
                var billing = document.Kind.IsSales() ? (BillingViewModel)Create<SalesBillingViewModel>() : Create<PurchaseBillingViewModel>();
                await billing.LoadAsync();
                billing.Section = billing.Sections.First(s => s.Key == (document.Kind.IsReturn() ? "returns" : "documents"));
                billing.Preview = document;
                page = billing;
                break;
            case DrillKind.Settlement:
                await services.GetRequiredService<PrintActions>().PreviewAsync(new SettlementSubject(target.Id));
                return;
            case DrillKind.Voucher:
                var voucher = Create<VouchersViewModel>();
                await voucher.LoadAsync();
                await voucher.OpenAsync(target.Id);
                page = voucher;
                break;
            case DrillKind.Product:
                var products = Create<ProductsViewModel>();
                products.ShowArchived = true;
                await products.LoadAsync();
                products.Selected = products.Products.FirstOrDefault(p => p.Id == target.Id);
                page = products;
                break;
            case DrillKind.Party:
                var party = await services.GetRequiredService<PartyService>().GetAsync(target.Id);
                var parties = party.Kind == PartyKind.Customer ? (PartiesViewModel)Create<CustomersViewModel>() : Create<SuppliersViewModel>();
                await parties.LoadAsync();
                parties.Selected = party;
                page = parties;
                break;
            case DrillKind.Expense:
                var expense = Create<ExpensesViewModel>();
                await expense.LoadAsync();
                expense.Detail = await services.GetRequiredService<ExpenseService>().GetAsync(target.Id);
                page = expense;
                break;
            case DrillKind.ServiceJob:
                var service = Create<ServiceViewModel>();
                await service.LoadAsync();
                service.Detail = await services.GetRequiredService<ServiceJobService>().GetAsync(target.Id);
                page = service;
                break;
            case DrillKind.StockDocument:
                var stock = Create<StockViewModel>();
                stock.Section = stock.Sections.First(s => s.Key == "documents");
                await stock.LoadAsync();
                stock.SelectedDocument = stock.Documents.FirstOrDefault(d => d.Id == target.Id);
                page = stock;
                break;
            default: throw new WorkspaceValidationException("Open this account in the Ledger statement report.");
        }
        var window = new Window { Title = "QIK Suite · Source detail", Width = 1200, Height = 760, MinWidth = 900, MinHeight = 600,
            Owner = System.Windows.Application.Current.MainWindow, Content = new ContentControl { Content = page } };
        window.SetResourceReference(Window.BackgroundProperty, "BackgroundBrush");
        window.ShowDialog();
    }
    private T Create<T>() where T : ILoadable => ActivatorUtilities.CreateInstance<T>(services);
}
