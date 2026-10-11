using Microsoft.Data.Sqlite;
using Nexora.Accounting;
using Nexora.Application;
using Nexora.Application.Billing;
using Nexora.Application.Crm;
using Nexora.Application.Inventory;
using Nexora.Application.Security;
using Nexora.Application.Service;
using Nexora.Domain.Billing;
using Nexora.Domain.Inventory;
using Nexora.Domain.Service;
using Nexora.Tests.Billing;

namespace Nexora.Tests.Phase7;

/// <summary>Service jobs on the billing fixture. PHONE-1 is serial-tracked with a 12-month warranty; IMEI-1 was sold to Meera on 10-10-2026.</summary>
public abstract class ServiceTestBase : BillingTestBase
{
    protected ServiceJobService Service => Db.Get<ServiceJobService>();

    protected Guid Phone { get; private set; }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        Phone = await Catalogue.SaveProductAsync(new ProductInput(null, new ProductDetails("PHONE-1", "Smartphone", HsnCode: "8517", TaxCategoryId: Gst18,
            PurchasePrice: 8_000, SellingPrice: 10_000, WarrantyMonths: 12), Units["PCS"], TrackingMode.Serial, false));
        await ReceiveAsync(Phone, 1, 8_000, serial: "IMEI-1");
        await ReceiveAsync(Phone, 1, 8_000, serial: "IMEI-2");
        await SellAsync(Local, new BillingLineInput(Phone, 1, SerialNumber: "IMEI-1"));
    }

    protected Task<Guid> JobAsync(string serial = "", DateOnly? received = null, string item = "LED panel flickering") =>
        Service.CreateAsync(new ServiceJobInput(null, received ?? October, Local, item, "Flickers after 10 minutes", Main, SerialNumber: serial));
}

public sealed class ServiceWorkflowTests : ServiceTestBase
{
    [Fact]
    public async Task Warranty_is_derived_from_the_sale_of_the_serial_and_the_product_warranty()
    {
        var covered = await JobAsync("imei-1");
        var unknown = await JobAsync("NOT-SOLD-HERE");
        var later = await JobAsync("IMEI-1", received: October.AddMonths(13));

        var job = await Service.GetAsync(covered);
        Assert.Equal(("JOB/2026-27/0001", "IMEI-1", ServiceCharge.Warranty, new DateOnly(2027, 10, 10)), (job.Number, job.SerialNumber, job.Charge, job.WarrantyUntil));
        Assert.Equal(Phone, job.ProductId);
        Assert.Equal((ServiceCharge.Chargeable, (DateOnly?)null), ((await Service.GetAsync(unknown)).Charge, (await Service.GetAsync(unknown)).WarrantyUntil));
        Assert.Equal(ServiceCharge.Chargeable, (await Service.GetAsync(later)).Charge);
        var info = await Service.WarrantyAsync("IMEI-1");
        Assert.Equal((12, October), (info!.WarrantyMonths, info.SoldOn!.Value));
        Assert.Null(await Service.WarrantyAsync("IMEI-2"));                      // in stock, never sold
    }

    [Fact]
    public async Task Status_follows_the_workflow_and_closed_jobs_are_final()
    {
        var id = await JobAsync();

        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Service.MoveAsync(id, ServiceStatus.Ready));
        await Service.MoveAsync(id, ServiceStatus.Diagnosed);
        await Service.MoveAsync(id, ServiceStatus.AwaitingParts);
        await Service.MoveAsync(id, ServiceStatus.InRepair);
        await Service.MoveAsync(id, ServiceStatus.Ready);
        Assert.Equal([ServiceStatus.Delivered, ServiceStatus.InRepair], (await Service.GetAsync(id)).NextStatuses);
        await Service.MoveAsync(id, ServiceStatus.Delivered, date: October.AddDays(3));

        var job = await Service.GetAsync(id);
        Assert.Equal((ServiceStatus.Delivered, October.AddDays(3)), (job.Status, job.DeliveredOn!.Value));
        Assert.Empty(job.NextStatuses);
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Service.MoveAsync(id, ServiceStatus.InRepair));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Service.UpdateAsync(new ServiceJobInput(id, October, Local, "Changed", "x", Main)));
        using var connection = Db.OpenRawConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE ServiceJobs SET Fault = 'edited'";
        Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());
    }

    [Fact]
    public async Task Cancelling_needs_a_reason_and_no_parts_left_on_the_job()
    {
        var id = await JobAsync();
        var part = await Service.IssuePartAsync(id, new ServicePartInput(Led, 1, October));

        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Service.MoveAsync(id, ServiceStatus.Cancelled, "Customer withdrew"));
        await Service.ReturnPartAsync(part, October);
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Service.MoveAsync(id, ServiceStatus.Cancelled, " "));
        await Service.MoveAsync(id, ServiceStatus.Cancelled, "Customer withdrew");

        Assert.Equal(ServiceStatus.Cancelled, (await Service.GetAsync(id)).Status);
        Assert.Equal(20m, await OnHandAsync(Led));
    }

    [Fact]
    public async Task History_lists_jobs_per_customer_and_per_serial()
    {
        await JobAsync("IMEI-1");
        await JobAsync("IMEI-1", received: October.AddDays(30));
        await Service.CreateAsync(new ServiceJobInput(null, October, Bengaluru, "Fan", "Noisy", Main, SerialNumber: "FAN-9"));

        Assert.Equal(2, (await Service.HistoryAsync(null, "imei-1")).Count);
        Assert.Equal(2, (await Service.HistoryAsync(Local, null)).Count);
        Assert.Single(await Service.HistoryAsync(Bengaluru, null));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Service.HistoryAsync(null, " "));
    }
}

public sealed class ServicePartsAndBillingTests : ServiceTestBase
{
    [Fact]
    public async Task Chargeable_job_issues_parts_through_stock_and_invoices_parts_and_labour_once()
    {
        var id = await JobAsync();
        await Service.IssuePartAsync(id, new ServicePartInput(Led, 2, October));
        await Service.AddLabourAsync(id, new ServiceLabourInput("Driver replacement and testing", 1, 500, "998719", Gst18));

        Assert.Equal(18m, await OnHandAsync(Led));
        Assert.Equal(200m + 8_000m, await LedgerAsync(SystemAccounts.CostOfGoodsSold, October));   // the parts, plus the phone sold in setup
        await AssertBooksBalanceAsync();

        var invoiceId = await Service.CreateInvoiceAsync(id);
        var draft = await Billing.GetAsync(invoiceId);
        Assert.Equal([BillingLineType.IssuedPart, BillingLineType.Service], draft.Lines.Select(l => l.LineType));
        Assert.Equal((150m, 500m, "998719"), (draft.Lines[0].Rate, draft.Lines[1].Rate, draft.Lines[1].HsnCode));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Service.CreateInvoiceAsync(id));
        foreach (var status in new[] { ServiceStatus.Diagnosed, ServiceStatus.InRepair, ServiceStatus.Ready }) await Service.MoveAsync(id, status);
        var early = await Assert.ThrowsAsync<WorkspaceValidationException>(() => Service.MoveAsync(id, ServiceStatus.Delivered));
        Assert.Contains("post the invoice", early.Message, StringComparison.Ordinal);

        await Billing.PostAsync(invoiceId);

        var invoice = await Billing.GetAsync(invoiceId);
        Assert.Equal((800m, 72m, 72m, 944m), (invoice.Taxable, invoice.Cgst, invoice.Sgst, invoice.Total));
        Assert.Equal(18m, await OnHandAsync(Led));                                   // the invoice did not move stock again
        Assert.Equal(200m + 8_000m, await LedgerAsync(SystemAccounts.CostOfGoodsSold, October));   // parts cost once (+ the phone sold in setup)
        Assert.Equal(-(10_000m + 800m), await LedgerAsync(SystemAccounts.Sales, October));
        await AssertBooksBalanceAsync();
        await Service.MoveAsync(id, ServiceStatus.Delivered);
        Assert.Equal(ServiceStatus.Delivered, (await Service.GetAsync(id)).Status);
    }

    [Fact]
    public async Task Warranty_job_charges_nothing_but_parts_still_leave_stock_into_warranty_costs()
    {
        var id = await JobAsync("IMEI-1");
        await Service.IssuePartAsync(id, new ServicePartInput(Led, 1, October));

        Assert.Equal(19m, await OnHandAsync(Led));
        Assert.Equal(100m, await LedgerAsync(SystemAccounts.WarrantyExpense, October));
        Assert.Equal(8_000m, await LedgerAsync(SystemAccounts.CostOfGoodsSold, October));    // only the phone sold in setup
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Service.CreateInvoiceAsync(id));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Service.SetChargeAsync(id, ServiceCharge.Chargeable));
        foreach (var status in new[] { ServiceStatus.Diagnosed, ServiceStatus.InRepair, ServiceStatus.Ready, ServiceStatus.Delivered })
            await Service.MoveAsync(id, status);
        Assert.Equal(ServiceStatus.Delivered, (await Service.GetAsync(id)).Status);
        await AssertBooksBalanceAsync();
    }

    [Fact]
    public async Task Returned_parts_come_back_at_their_cost_and_billed_parts_cannot_be_returned()
    {
        var id = await JobAsync();
        var first = await Service.IssuePartAsync(id, new ServicePartInput(Led, 2, October));
        var second = await Service.IssuePartAsync(id, new ServicePartInput(Led, 1, October));

        await Service.ReturnPartAsync(second, October);

        Assert.Equal(18m, await OnHandAsync(Led));
        Assert.Equal(200m, await LedgerAsync(SystemAccounts.CostOfGoodsSold, October) - 8_000m);
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Service.ReturnPartAsync(second, October));
        var invoice = await Service.CreateInvoiceAsync(id);
        Assert.Single((await Billing.GetAsync(invoice)).Lines);                     // the returned part is not billed
        await Billing.PostAsync(invoice);
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Service.ReturnPartAsync(first, October));
        Assert.True((await Service.GetAsync(id)).Parts.Single(p => p.Id == first).IsBilled);
        await AssertBooksBalanceAsync();
    }

    [Fact]
    public async Task Parts_follow_stock_rules_for_quantity_and_serials()
    {
        var id = await JobAsync();

        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Service.IssuePartAsync(id, new ServicePartInput(Led, 25, October)));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Service.IssuePartAsync(id, new ServicePartInput(Phone, 1, October, SerialNumber: "IMEI-1")));   // sold, not in stock
        await Service.IssuePartAsync(id, new ServicePartInput(Phone, 1, October, SerialNumber: "IMEI-2"));
        Assert.DoesNotContain(await Stock.BalancesAsync(), b => b.ProductId == Phone);
        var stockDocument = (await Stock.DocumentsAsync()).First(d => d.Type == StockDocumentType.ServiceIssue);
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Stock.ReverseAsync(stockDocument.Id, October, ""));
        Assert.True((await Stock.ReconcileAsync(October)).IsReconciled);
    }

    [Fact]
    public async Task Labour_cannot_change_once_the_job_is_invoiced()
    {
        var id = await JobAsync();
        var labour = await Service.AddLabourAsync(id, new ServiceLabourInput("Inspection", 1, 300));
        var invoice = await Service.CreateInvoiceAsync(id);

        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Service.RemoveLabourAsync(labour));
        await Billing.DeleteDraftAsync(invoice);
        await Service.RemoveLabourAsync(labour);
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Service.CreateInvoiceAsync(id));          // nothing left to invoice
    }
}

public sealed class ServiceLineBillingTests : ServiceTestBase
{
    [Fact]
    public async Task Service_only_invoices_post_without_stock_and_can_be_credited()
    {
        var id = await Billing.SaveDraftAsync(new BillingDraftInput(null, BillingDocumentKind.SalesInvoice, October, Local, Main, SettlementMode.Credit,
            [new BillingLineInput(Guid.Empty, 2, Rate: 250, Description: "Annual maintenance visit", LineType: BillingLineType.Service, TaxCategoryId: Gst18, HsnCode: "9987")]));
        await Billing.PostAsync(id);

        var invoice = await Billing.GetAsync(id);
        Assert.Equal((500m, 590m), (invoice.Taxable, invoice.Total));
        Assert.Equal(string.Empty, Assert.Single(invoice.Lines).Sku);
        var (credit, _) = await ReturnAsync(id, (1, 1));
        Assert.Equal(295m, (await Billing.GetAsync(credit)).Total);
        await AssertBooksBalanceAsync();
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Billing.SaveDraftAsync(new BillingDraftInput(null, BillingDocumentKind.PurchaseBill, October, Vendor, Main,
            SettlementMode.Credit, [new BillingLineInput(Guid.Empty, 1, Rate: 100, Description: "Consulting", LineType: BillingLineType.Service)])));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Billing.SaveDraftAsync(new BillingDraftInput(null, BillingDocumentKind.SalesInvoice, October, Local, Main,
            SettlementMode.Credit, [new BillingLineInput(Led, 1, LineType: BillingLineType.IssuedPart)])));     // issued parts come only from a job
    }
}

public sealed class MaintenanceTests : ServiceTestBase
{
    [Fact]
    public async Task Reminders_become_follow_up_tasks_once_per_due_date()
    {
        var schedule = await Service.SaveScheduleAsync(new MaintenanceInput(null, Local, "Inverter AMC", 6, October.AddDays(5), SerialNumber: "INV-77"));

        var created = await Service.GenerateRemindersAsync(October);
        var again = await Service.GenerateRemindersAsync(October);

        Assert.Equal((1, 0), (created, again));
        var task = Assert.Single(await Db.Get<CrmService>().FollowUpsAsync(new FollowUpFilter(CustomerId: Local)));
        Assert.Equal(("Maintenance due: Inverter AMC (INV-77)", October.AddDays(5)), (task.Title, task.DueDate));
        Assert.Equal(October.AddDays(5).AddMonths(6), (await Service.SchedulesAsync(Local)).Single(s => s.Id == schedule).NextDueDate);
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Service.SaveScheduleAsync(new MaintenanceInput(null, Local, "Bad", 0, October)));
    }
}

public sealed class ServiceAccessTests : ServiceTestBase
{
    [Fact]
    public async Task Service_staff_run_jobs_but_cannot_invoice_and_billing_operators_cannot_see_service()
    {
        var id = await JobAsync();
        await SignInAsAsync("sam", "Service Staff");

        await Service.IssuePartAsync(id, new ServicePartInput(Led, 1, October));
        await Service.MoveAsync(id, ServiceStatus.Diagnosed);
        await Assert.ThrowsAsync<AccessDeniedException>(() => Service.CreateInvoiceAsync(id));
        Assert.Null(Assert.Single((await Service.GetAsync(id)).Parts).Cost);         // no cost figures without inventory.view-cost

        await SignInAsAdministratorAsync();
        await SignInAsAsync("bina", "Billing Operator");
        await Assert.ThrowsAsync<AccessDeniedException>(() => Service.ListAsync());
        await Assert.ThrowsAsync<AccessDeniedException>(() => JobAsync());
    }

    [Fact]
    public async Task Another_companys_jobs_are_out_of_reach_and_part_movements_are_append_only()
    {
        var id = await JobAsync();
        await Service.IssuePartAsync(id, new ServicePartInput(Led, 1, October));
        using (var connection = Db.OpenRawConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE ServiceParts SET CostPaise = 1";
            Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());
        }

        var demo = (await Db.Workspace.GetCompaniesAsync()).Single(c => c.IsDemo);
        await Db.Workspace.SwitchCompanyAsync(demo.Id);
        Assert.Empty(await Service.ListAsync(includeClosed: true));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Service.GetAsync(id));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Service.IssuePartAsync(id, new ServicePartInput(Led, 1, October)));
    }
}

public sealed class Phase7UpgradeTests : ServiceTestBase
{
    /// <summary>Companies created before Phase 7 have no warranty account; the first warranty part adds it under Indirect Expenses.</summary>
    [Fact]
    public async Task Older_companies_get_the_warranty_account_on_first_use_and_their_settings_on_start()
    {
        using (var connection = Db.OpenRawConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "DELETE FROM Accounts WHERE SystemKey = 'warranty-expense'; DELETE FROM ServiceSettings; DELETE FROM ExpenseSettings; "
                + "DELETE FROM NumberingRules WHERE DocumentType IN ('service.job', 'expense.voucher');";
            command.ExecuteNonQuery();
        }

        await Db.Workspace.InitializeAsync();
        await SignInAsAdministratorAsync();
        var id = await JobAsync("IMEI-1");
        await Service.IssuePartAsync(id, new ServicePartInput(Led, 1, October));

        var account = (await Db.Get<ChartOfAccountsService>().ListAsync()).Single(a => a.Name == "Warranty and Service Costs");
        Assert.Equal((await Db.Get<ChartOfAccountsService>().ListAsync()).Single(a => a.Name == "Indirect Expenses").Id, account.ParentId);
        Assert.Equal("JOB/2026-27/0001", (await Service.GetAsync(id)).Number);
        Assert.False(await Db.Get<Application.Expenses.ExpenseService>().AllowSelfApprovalAsync());
        await AssertBooksBalanceAsync();
    }
}


public sealed class ServicePrintTests : ServiceTestBase
{
    [Fact]
    public async Task Job_card_and_service_invoice_print_through_the_print_centre()
    {
        var printing = Db.Get<Nexora.Printing.PrintingService>();
        var id = await Service.CreateAsync(new ServiceJobInput(null, October, Local, "Smartphone", "Does not charge", Main, SerialNumber: "IMEI-1",
            Accessories: "Charger, back cover"));
        await Service.IssuePartAsync(id, new ServicePartInput(Led, 1, October));

        var card = (await printing.PreviewAsync(new Nexora.Printing.ServiceJobSubject(id))).AllText;

        Assert.Contains("SERVICE JOB CARD", card, StringComparison.Ordinal);
        Assert.Contains("JOB/2026-27/0001", card, StringComparison.Ordinal);
        Assert.Contains("Smartphone · Serial IMEI-1", card, StringComparison.Ordinal);
        Assert.Contains("Does not charge", card, StringComparison.Ordinal);
        Assert.Contains("Charger, back cover", card, StringComparison.Ordinal);
        Assert.Contains("Under warranty until 10-10-2027", card, StringComparison.Ordinal);
        await printing.PrintAsync(new Nexora.Printing.ServiceJobSubject(id));
        Assert.Contains("DUPLICATE", (await printing.PreviewAsync(new Nexora.Printing.ServiceJobSubject(id))).AllText, StringComparison.Ordinal);

        var chargeable = await JobAsync();
        await Service.IssuePartAsync(chargeable, new ServicePartInput(Led, 1, October));
        await Service.AddLabourAsync(chargeable, new ServiceLabourInput("Labour: driver replacement", 1, 500, "9987", Gst18));
        var invoice = await Service.CreateInvoiceAsync(chargeable);
        await Billing.PostAsync(invoice);
        var text = (await printing.PreviewAsync(new Nexora.Printing.BillingSubject(invoice))).AllText;
        Assert.Contains("Labour: driver replacement", text, StringComparison.Ordinal);
        Assert.Contains("9987", text, StringComparison.Ordinal);
        Assert.Contains("₹ 767.00", text, StringComparison.Ordinal);       // 150 + 500 = 650 + 18% = 767
    }
}
