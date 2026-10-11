using Microsoft.Data.Sqlite;
using Nexora.Application;
using Nexora.Application.Audit;
using Nexora.Application.Billing;
using Nexora.Application.Crm;
using Nexora.Application.Security;
using Nexora.Domain.Billing;
using Nexora.Domain.Crm;
using Nexora.Tests.Billing;

namespace Nexora.Tests.Phase7;

public abstract class CrmTestBase : BillingTestBase
{
    protected CrmService Crm => Db.Get<CrmService>();

    protected async Task<int> CustomerCountAsync() => (await Parties.ListAsync(PartyKind.Customer, includeInactive: true)).Count;
}

public sealed class LeadTests : CrmTestBase
{
    [Fact]
    public async Task Converting_a_lead_creates_a_customer_and_moves_its_enquiries_and_follow_ups()
    {
        var lead = await Crm.SaveLeadAsync(new LeadInput(null, "Ritu Desai", "Desai Interiors", "9876543210", "ritu@desai.example", LeadSource.Referral));
        await Crm.SetLeadStatusAsync(lead, LeadStatus.Qualified);
        var enquiry = await Crm.SaveEnquiryAsync(new EnquiryInput(null, October, lead, null, "Quote for 40 LED panels", ProductId: Led));
        await Crm.SaveFollowUpAsync(new FollowUpInput(null, "Send quotation", October.AddDays(2), LeadId: lead));
        Assert.True((await Crm.LeadsAsync()).Single().IsProspect);

        var customer = await Crm.ConvertLeadAsync(lead);

        var party = await Parties.GetAsync(customer);
        Assert.Equal(("Ritu Desai", "9876543210", "ritu@desai.example"), (party.Name, party.Phone, party.Email));
        var row = (await Crm.LeadsAsync(includeClosed: true)).Single();
        Assert.Equal((LeadStatus.Converted, customer), (row.Status, row.CustomerId));
        Assert.Equal(customer, (await Crm.EnquiriesAsync(customerId: customer)).Single(e => e.Id == enquiry).CustomerId);
        Assert.Single(await Crm.FollowUpsAsync(new FollowUpFilter(CustomerId: customer)));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Crm.ConvertLeadAsync(lead));
        Assert.Empty(await Crm.LeadsAsync());                                  // closed leads are hidden by default
    }

    [Fact]
    public async Task Conversion_never_duplicates_a_customer_with_the_same_name_phone_or_email()
    {
        var before = await CustomerCountAsync();
        var byName = await Crm.SaveLeadAsync(new LeadInput(null, "meera electricals"));
        var byPhone = await Crm.SaveLeadAsync(new LeadInput(null, "Meera E (shop)", Phone: "+91 98200 12345"));

        var error = await Assert.ThrowsAsync<PossibleDuplicateException>(() => Crm.ConvertLeadAsync(byName));
        await Assert.ThrowsAsync<PossibleDuplicateException>(() => Crm.ConvertLeadAsync(byPhone));

        Assert.Equal(Local, Assert.Single(error.Candidates).Id);
        Assert.Equal(before, await CustomerCountAsync());
        Assert.Equal(Local, await Crm.ConvertLeadAsync(byName, linkToCustomerId: Local));
        Assert.Equal(before, await CustomerCountAsync());
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Crm.ConvertLeadAsync(byPhone, linkToCustomerId: Vendor));
    }

    [Fact]
    public async Task Lost_leads_need_a_reason_and_must_be_reopened_before_converting()
    {
        var lead = await Crm.SaveLeadAsync(new LeadInput(null, "Cold Call Co"));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Crm.MarkLeadLostAsync(lead, ""));
        await Crm.MarkLeadLostAsync(lead, "Bought elsewhere");

        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Crm.ConvertLeadAsync(lead));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Crm.SetLeadStatusAsync(lead, LeadStatus.Converted));
        await Crm.SetLeadStatusAsync(lead, LeadStatus.Contacted);
        Assert.Equal(LeadStatus.Contacted, (await Crm.LeadsAsync()).Single().Status);
    }

    [Fact]
    public async Task Contacts_keep_one_primary_per_party()
    {
        await Crm.SaveContactAsync(new ContactInput(null, Local, "Meera Shah", "Owner", IsPrimary: true));
        await Crm.SaveContactAsync(new ContactInput(null, Local, "Arun", "Accounts", IsPrimary: true));
        await Crm.SaveContactAsync(new ContactInput(null, Vendor, "Pune desk", IsPrimary: true));

        var contacts = await Crm.ContactsAsync(Local);
        Assert.Equal(["Arun", "Meera Shah"], contacts.Select(c => c.Name));
        Assert.Equal([true, false], contacts.Select(c => c.IsPrimary));
        Assert.True(Assert.Single(await Crm.ContactsAsync(Vendor)).IsPrimary);
    }
}

public sealed class FollowUpAndTimelineTests : CrmTestBase
{
    [Fact]
    public async Task Follow_ups_are_overdue_when_past_due_and_summarised_for_their_assignee()
    {
        var asha = (await Crm.MembersAsync()).Single(m => m.DisplayName == "Asha Rao").UserId;
        Db.Clock.UtcNow = new DateTime(2026, 10, 10, 6, 0, 0, DateTimeKind.Utc);
        var today = DateOnly.FromDateTime(Db.Clock.UtcNow.ToLocalTime());
        await Crm.SaveFollowUpAsync(new FollowUpInput(null, "Call back Meera", today.AddDays(-2), asha, CustomerId: Local));
        await Crm.SaveFollowUpAsync(new FollowUpInput(null, "Visit site", today, asha, CustomerId: Local));
        await Crm.SaveFollowUpAsync(new FollowUpInput(null, "Next week", today.AddDays(7), asha));

        var summary = await Crm.MySummaryAsync();

        Assert.Equal((1, 1, 2), (summary.Overdue, summary.DueToday, summary.Items.Count));
        Assert.Equal("Overdue", summary.Items[0].State);
        await Crm.SetFollowUpDoneAsync(summary.Items[0].Id, true);
        Assert.Equal(0, (await Crm.MySummaryAsync()).Overdue);
        Assert.Equal(3, (await Crm.FollowUpsAsync(new FollowUpFilter(IncludeDone: true))).Count);
    }

    [Fact]
    public async Task Only_the_assignee_or_a_crm_manager_ticks_off_a_follow_up()
    {
        var task = await Crm.SaveFollowUpAsync(new FollowUpInput(null, "Admin's task", October));
        await SignInAsAsync("sam", "Service Staff");

        await Assert.ThrowsAsync<AccessDeniedException>(() => Crm.SetFollowUpDoneAsync(task, true));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Crm.SaveLeadAsync(new LeadInput(null, "Nope")));
        Assert.Single(await Crm.FollowUpsAsync(new FollowUpFilter()));
        Assert.Equal(0, (await Crm.MySummaryAsync()).Overdue);
    }

    [Fact]
    public async Task Timeline_merges_calls_enquiries_follow_ups_sales_and_lead_history()
    {
        var lead = await Crm.SaveLeadAsync(new LeadInput(null, "Kiran Patil", Phone: "9000000001"));
        await Crm.AddActivityAsync(lead, null, ActivityKind.Call, Db.Clock.UtcNow.AddDays(-3), "Asked about LED panels");
        var customer = await Crm.ConvertLeadAsync(lead);
        await Crm.AddActivityAsync(null, customer, ActivityKind.Visit, Db.Clock.UtcNow.AddHours(-1), "Showroom visit");
        await SellAsync(customer, new BillingLineInput(Led, 1));

        var timeline = await Crm.TimelineAsync(null, customer);

        Assert.Contains(timeline, e => e.Kind == "Call" && e.Detail == "Asked about LED panels");     // recorded while it was a lead
        Assert.Contains(timeline, e => e.Kind == "Visit");
        Assert.Contains(timeline, e => e.Kind == "Sales invoice" && e.Title.StartsWith("INV/", StringComparison.Ordinal));
        Assert.Equal(timeline.OrderByDescending(e => e.WhenUtc), timeline);
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Crm.AddActivityAsync(null, customer, ActivityKind.Call, Db.Clock.UtcNow.AddDays(2), "Future"));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Crm.AddActivityAsync(lead, customer, ActivityKind.Note, Db.Clock.UtcNow, "Both"));
    }

    [Fact]
    public async Task Customer_view_reads_balances_purchases_payments_and_service_from_their_own_records()
    {
        await Parties.SaveAsync(new PartyInput(Local, PartyKind.Customer, new PartyDetails("Meera Electricals", "9820012345", StateCode: "27", CreditLimit: 5_000)));
        var (invoice, _) = await SellAsync(Local, new BillingLineInput(Led, 3));       // 531
        await Settlements.PostAsync(new SettlementInput(SettlementKind.CustomerReceipt, October, Local, CashAccount, 200, PaymentMethod.Cash, [new AllocationInput(invoice, 200)]));

        var overview = await Crm.CustomerAsync(Local);

        var party = await Parties.GetAsync(Local);
        Assert.Equal((party.LedgerBalance, party.Outstanding, 5_000m), (overview.Customer.LedgerBalance, overview.Customer.Outstanding, overview.Customer.CreditLimit));
        Assert.Equal(5_000m - 331m, overview.CreditAvailable);
        Assert.Single(overview.Invoices);
        Assert.Single(overview.Payments);
        Assert.True(overview.CanSeeSales && overview.CanSeeService);

        await SignInAsAsync("sam", "Service Staff");                            // CRM view but no sales view
        var limited = await Crm.CustomerAsync(Local);
        Assert.False(limited.CanSeeSales);
        Assert.Empty(limited.Invoices);
        Assert.Equal(0m, limited.Customer.LedgerBalance);
    }
}

public sealed class CreditLimitAndPricingTests : CrmTestBase
{
    [Fact]
    public async Task Credit_sales_above_the_limit_are_refused_until_overridden_with_permission()
    {
        await Parties.SaveAsync(new PartyInput(Local, PartyKind.Customer, new PartyDetails("Meera Electricals", "9820012345", StateCode: "27", CreditLimit: 500)));
        var draft = await DraftAsync(BillingDocumentKind.SalesInvoice, Local, new BillingLineInput(Led, 3));       // 531

        var warning = await Assert.ThrowsAsync<CreditLimitExceededException>(() => Billing.PostAsync(draft));

        Assert.Equal((500m, 531m), (warning.Limit, warning.Exposure));
        Assert.Contains("31.00 over the limit", warning.Message, StringComparison.Ordinal);
        Assert.Equal(BillingStatus.Draft, (await Billing.GetAsync(draft)).State);
        Assert.Equal(20m, await OnHandAsync(Led));

        await SignInAsAsync("bina", "Billing Operator");
        await Assert.ThrowsAsync<CreditLimitExceededException>(() => Billing.PostAsync(draft));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Billing.PostAsync(draft, overrideCreditLimit: true));

        await SignInAsAdministratorAsync();
        Assert.StartsWith("INV/", await Billing.PostAsync(draft, overrideCreditLimit: true), StringComparison.Ordinal);
        Assert.Contains(await Db.Get<AuditService>().QueryAsync(new AuditQuery()), e => e.Action == "CreditLimitOverridden");
    }

    [Fact]
    public async Task Sales_within_the_limit_cash_sales_and_customers_without_a_limit_post_normally()
    {
        await Parties.SaveAsync(new PartyInput(Local, PartyKind.Customer, new PartyDetails("Meera Electricals", "9820012345", StateCode: "27", CreditLimit: 1_000)));

        await SellAsync(Local, new BillingLineInput(Led, 2));                   // 354 within 1,000
        await CashSaleAsync(new BillingLineInput(Led, 10));                     // cash: no limit
        await SellAsync(Bengaluru, new BillingLineInput(Led, 5));               // no limit set
        await Assert.ThrowsAsync<CreditLimitExceededException>(() => SellAsync(Local, new BillingLineInput(Led, 4)));   // 354 + 708 > 1,000
    }

    [Fact]
    public async Task Customer_prices_become_the_default_rate_for_that_customer_only()
    {
        await Crm.SetCustomerPriceAsync(Local, Led, 140);
        await SignInAsAsync("bina", "Billing Operator");                        // no price-override permission

        var (mine, _) = await SellAsync(Local, new BillingLineInput(Led, 1));
        var (other, _) = await SellAsync(Bengaluru, new BillingLineInput(Led, 1));

        Assert.Equal(140m, Assert.Single((await Billing.GetAsync(mine)).Lines).Rate);
        Assert.Equal(150m, Assert.Single((await Billing.GetAsync(other)).Lines).Rate);
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => SellAsync(Local, new BillingLineInput(Led, 1, Rate: 150)));   // 150 is now an override for Meera
        await Assert.ThrowsAsync<AccessDeniedException>(() => Crm.SetCustomerPriceAsync(Local, Led, 100));
        Assert.Equal(140m, Assert.Single(await Crm.CustomerPricesAsync(Local)).Price);
    }
}

public sealed class CrmIsolationTests : CrmTestBase
{
    [Fact]
    public async Task Another_companys_crm_records_are_out_of_reach_and_activities_are_append_only()
    {
        var lead = await Crm.SaveLeadAsync(new LeadInput(null, "Private Lead"));
        await Crm.AddActivityAsync(lead, null, ActivityKind.Note, Db.Clock.UtcNow, "Confidential");
        using (var connection = Db.OpenRawConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE Activities SET Summary = 'edited'";
            Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());
        }

        var demo = (await Db.Workspace.GetCompaniesAsync()).Single(c => c.IsDemo);
        await Db.Workspace.SwitchCompanyAsync(demo.Id);
        Assert.Empty(await Crm.LeadsAsync(includeClosed: true));
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Crm.ConvertLeadAsync(lead));
        Assert.Empty(await Crm.TimelineAsync(lead, null));
    }
}
